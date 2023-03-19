module AsyncExtensions
open System
open FSharp.Control
open Orleans.Streams
open System.Threading.Tasks
open System.Threading.Channels

module AsyncSeq =
    type private D<'t> = | Data of 't | Err of Exception | Done

    ///Returns a tuple of IAsyncOberver<'t> and AsyncSeq<'t>. 
    ///The IAsyncObsever<'t> is to be 'resumed' from IStreamSubscriptionObserver.OnSubscribe.
    ///After which data will be fed to the AsyncSeq<'t>.
    ///Note: The producer-conusumer processing is done with BoundedChannel.
    ///Should only be used with single reader/writer.
    ///Default 'fullMode' is to block producer and wait.
    ///(see Microsoft documentation on Channels)
    let createAsyncObserver<'t>(capacity, fullMode) =
        let ops = BoundedChannelOptions(capacity,
                        SingleReader = true,
                        FullMode = (fullMode |> Option.defaultValue BoundedChannelFullMode.Wait),
                        SingleWriter = true)
        let qs = Channel.CreateBounded<D<'t>>(ops) 
        let obs =
                 {new IAsyncObserver<'t> with
                    member this.OnCompletedAsync() =  qs.Writer.TryWrite(Done) |> ignore; qs.Writer.TryComplete() |> ignore; Task.CompletedTask
                    member this.OnErrorAsync(ex) = qs.Writer.WriteAsync(Err ex).AsTask()
                    member this.OnNextAsync(e:'t, token) = qs.Writer.WriteAsync(Data(e)).AsTask()}
        let seq =    
            asyncSeq {
                let mutable go = true
                while go do
                    match! qs.Reader.ReadAsync().AsTask() |> Async.AwaitTask with
                    | Data e -> yield e
                    | Err exn ->raise exn
                    | Done -> go <- false
            }
        obs,seq

