namespace OKGrains
open System
open FsOrleansKafka
open System.Collections.Generic
open Orleans.Streams
open Orleans.Runtime
open Confluent.Kafka
open FSharp.Control
open System.Text.Json
open System.Text.Json.Serialization

module Serialization =
    let serOptions = 
        let o = JsonSerializerOptions()
        //other other serialization options here
        JsonFSharpOptions.Default()
            .WithAllowNullFields(true)
            .WithAllowOverride(true)
            .AddToJsonSerializerOptions(o)    
        o

    type JsonSerDe<'t>() =
        interface FsOrleansKafka.KafkaSerDe<'t>

        interface ISerializer<'t> with
            member this.Serialize(data, context) = JsonSerializer.SerializeToUtf8Bytes(data,options=serOptions)

        interface IDeserializer<'t> with
            member this.Deserialize(data, isNull, context) =
                try
                    JsonSerializer.Deserialize<'t>(data, options=serOptions)
                with ex ->
                    printfn $"Error deserializing ${System.Text.UTF8Encoding.Default.GetString(data)}"
                    raise ex

//simulator is used when a real Kafka instance is not available
module Simulator =
    let rng = Random()

    //generate simulated Kafka data
    let gen() =
        let aOrB = if rng.NextDouble() < 0.5 then "A" else "B"
        {
            Time = DateTime.Now
            Data =  rng.Next(1000) |> float
            Group = aOrB
        }
        |> Some

module Mapping =
    let beforeRead() =  async{return ()}     //return an async computation that performs any initial processing before data is read from Kafa (e.g. clean up files)
    
    let kafkaConfig() =
        ConsumerConfig(
            GroupId = "consumer1" //create the actual kafka connection configuration for your system. See Confluent.Kafka documentation
        )

    (*
    Note: 
        The 'mapper' function below filters and converts the kafka data to orleans stream data.
        To reduce the burden on orleans streams, filter the data as early as possible.
        This can be done by returning None for kafka data items that should not be processed.
        Also, typically Kafka data items may contain many more fields than what may be actually needed by a usecase.
        By only deserializing the required fields, the processing burden can be reduced. 
        This is easily done, if data is in Json format, by defining a type that contains
        only the required fields (i.e. ignoring the extra fields in the kafka record).

        Also, the mapper function is an AsyncSeq pipeline. This means additional processing
        such are buffering, accumulation, etc. can be done before data is 
        injected into the orleans stream. This can further reduce the burden on orleans.
    *)
    ///Convert the kafka data to orleans streams data (also does filtering)
    let mapper (inStream:AsyncSeq<KafkaInput>) : AsyncSeq<StreamData option> =
        inStream
        |> AsyncSeq.map(fun x -> 
            {
                Time = x.Time
                Data = x.Data
                GroupName = x.Group
            }
            |> Some
        )


///service to provide Kafka configuration via dependency injection
type KafkaConfigProvider() =
    let strCache = Dictionary<string,IAsyncStream<StreamData>>() //cache stream references

    member this.GetStream(sp:IStreamProvider,d:StreamData) =
        match strCache.TryGetValue(d.GroupName) with
        | true,s -> s                                                                   //used the cached 
        | _      -> 
            let streamId = StreamId.Create(Constants.STREAM_NAMESPACE,d.GroupName)      //a stream namespace may have multiple instances. The instance reference is the 2nd parm of StreamId.Create
            let str = sp.GetStream<StreamData>(streamId)                                //in our case we have two stream instances - "A and "B".  Once "A" is created, all 'A' data will go to that instance
            strCache.[d.GroupName] <- str                                               //And likewise for 'B'.
            str        

    interface IKafkaConfig<KafkaInput,StreamData> with
        member this.BeforeRead() = Mapping.beforeRead()
        member this.Generate() = Simulator.gen()                                    //only used in simulation mode
        member this.KafkaConfig = Mapping.kafkaConfig()
        member this.Mapper = Mapping.mapper
        member this.Serde = Serialization.JsonSerDe<KafkaInput>()                   //json deserializer
        member this.Simulate = true                                                 //set this to false to read from Kafa (instead of mocking the data)
        member this.StreamProviderId = Constants.STREAM_PROVIDER_NAME
        member this.Subscription = Topics [|"topic1"; "topic2"|]                    //Kafka topics that are subscribed to
        member this.WriteToStream sp u = this.GetStream(sp,u)                       //gets the right orleans stream for the given data
        



