namespace OKGrains
open System
open Orleans

///Data type read from the kafak queue
type KafkaInput =
    {
       Time: DateTime
       Data: float
       Group: string
    }

///Data type sent over Orleans streams - need serialization 'Id' tags
[<GenerateSerializer>]
[<CLIMutable>]
type StreamData =
    {
       [<Id(0u)>] Time      : DateTime
       [<Id(1u)>] Data      : float
       [<Id(2u)>] GroupName : string
    }


///Grain interface for the grain instances that will receive stream data
type IDataStream =
    inherit IGrainWithStringKey