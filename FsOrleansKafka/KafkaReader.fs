namespace FsOrleansKafka
open System
open System.Threading
open Confluent.Kafka
open FSharp.Control
open Orleans.Streams
open Orleans.Runtime
open System.Collections.Generic

type KafkaSub = Topics of string[] | Partitions of TopicPartition[]

type KafkaSerDe<'t> =
    inherit IDeserializer<'t>
    inherit ISerializer<'t>

type IKafkaConfig<'t,'u> =
    abstract member StreamProviderId : string
    abstract member WriteToStream    : IStreamProvider -> 'u -> IAsyncStream<'u>
    abstract member Mapper           : (AsyncSeq<'t> -> AsyncSeq<'u option>)
    abstract member BeforeRead       : unit -> Async<unit>
    abstract member KafkaConfig      : ConsumerConfig
    abstract member Subscription     : KafkaSub
    abstract member Serde            : KafkaSerDe<'t>
    abstract member Simulate         : bool       //if true, run in simulation mode - for local testing
    abstract member Generate         : unit -> 't option //generate simulated data for local testing

module KafkaReader =

    let getClient<'k,'v> (cfg:ConsumerConfig) (serde:IDeserializer<'v>) = 
        ConsumerBuilder<'k,'v>(cfg)
            .SetErrorHandler(fun c (e:Error) -> printfn $"%A{e.Reason}")
            .SetValueDeserializer(serde)
            .SetStatisticsHandler(fun c s -> printfn $"$%A{s}")
            .SetPartitionsAssignedHandler(fun c (ps:List<TopicPartition>) -> ps |> Seq.iter(fun x-> printfn $"add %A{x.Partition}"))
            .SetPartitionsRevokedHandler(fun c (ps:List<TopicPartitionOffset>) -> ps |> Seq.iter(fun x -> printfn $"lost %A{x.Partition}"))
            .Build()


    let read<'t,'u> (streamProvider:Lazy<IStreamProvider>) (cfg:IKafkaConfig<'t,'u>) (token:CancellationToken) =
        let mutable count = 0L
        let comp = 
            asyncSeq {                
                do! cfg.BeforeRead()
                use c = getClient<Ignore,'t> cfg.KafkaConfig cfg.Serde
                match cfg.Subscription with
                | Topics ts -> c.Subscribe(ts)
                | Partitions ps -> c.Assign(ps)
                while not token.IsCancellationRequested do
                    try
                        let cr = c.Consume(100) 
                        if cr <> null then                      
                            let prsd = Interlocked.Increment(&count)                            
                            if prsd % 50000L = 0L then printfn $"{Environment.MachineName}: read {prsd}"
                            let t = cr.Message.Value
                            yield t
                    with ex -> 
                        printfn "deserialization error"
                        ()

                printfn "done main loop"
            }
            |> cfg.Mapper
            |> AsyncSeq.choose (fun u -> u)
            |> AsyncSeq.iterAsync (fun u ->
                async {
                    let writeStream = cfg.WriteToStream streamProvider.Value u
                    do! writeStream.OnNextAsync(u) |> Async.AwaitTask
                })
        async {
            match! Async.Catch comp with
            | Choice2Of2 ex -> printfn $"KafkaReader: {ex.Message}\r\n{ex.StackTrace}"
            | Choice1Of2 _ -> ()
        }

    let readSim<'t,'u> (streamProvider:Lazy<IStreamProvider>) (cfg:IKafkaConfig<'t,'u>) (token:CancellationToken) =
        let mutable count = 0L
        let comp = 
            asyncSeq {        
                do! cfg.BeforeRead()
                while not token.IsCancellationRequested do
                    yield cfg.Generate()
                    do! Async.Sleep 10
                printfn "done main loop"
            }
            |> AsyncSeq.choose (fun x -> x)
            |> cfg.Mapper
            |> AsyncSeq.choose (fun u -> u)
            |> AsyncSeq.iterAsync (fun u ->
                async {
                    let writeStream = cfg.WriteToStream streamProvider.Value u
                    do! writeStream.OnNextAsync(u) |> Async.AwaitTask
                })
        async {
            match! Async.Catch comp with
            | Choice2Of2 ex -> printfn $"{ex.Message}\r\n{ex.StackTrace}"
            | Choice1Of2 _ -> ()
        }

