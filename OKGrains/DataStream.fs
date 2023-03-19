namespace OKGrains
open System
open System.Threading.Tasks
open Orleans
open Orleans.Streams
open FSharp.Control
open AsyncExtensions
open Orleans.Streams.Core

module Constants =
    let STREAM_PROVIDER_NAME = "main"           //name given to stream provider
    let [<Literal>] STREAM_NAMESPACE = "stream" //namespace for the data stream

///grain class for handling streaming data
[<ImplicitStreamSubscription(Constants.STREAM_NAMESPACE)>]          //implicit subscription to stream namespace (orleans will wire up the streaming data to this grain)
type DataStreamGrain() =
    inherit Grain()
    let obs,inStream = AsyncSeq.createAsyncObserver<StreamData>(10000,None) //converts Orleans stream to AsyncSeq

    ///process the incoming streamed data - contains the data processing pipeline
    let processStream() = 
        let pipeline =
            inStream
            |> AsyncSeq.bufferByCount 100             //we can use many operators from the AsyncSeq library to process streaming data
            |> AsyncSeq.iter (fun batch -> 
                //print the batch average (here we may also score a model and push the result to a new grain or a stream)
                let avgData = batch |> Array.averageBy(fun x->x.Data)
                let group = batch.[0].GroupName
                printfn $"Average: Group:{group} Avg:{avgData}"
            ) 
            |> Async.Catch
        async {
            match! pipeline with
            | Choice2Of2 ex -> printfn $"DataStreamGrain: %A{(ex.Message,ex.StackTrace)}"           //handle streaming data pipeline exception
            | _             -> ()
        }
        |> Async.Start

    interface IDataStream //need to 'implement' a grain interface for this to be activated

    override this.OnActivateAsync(token) =
        printfn $"DataStream grain activated {this.GetGrainId()}"
        processStream()                                             //activate the stream processing pipeline
        Task.CompletedTask

    override this.OnDeactivateAsync(reason,t) =
        printfn $"DataStream grain deactivated %A{reason}"
        base.OnDeactivateAsync(reason,t)


    //this interface is used when the grain subscription to the stream is activated
    interface IStreamSubscriptionObserver with
        member this.OnSubscribed(handleFactory) = 
            task{
                let handle = handleFactory.Create<StreamData>()
                let! _ = handle.ResumeAsync(obs)                    //send Orleans stream data to AsyncSeq stream processing pipeline
                return ()
            }
