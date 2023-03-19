namespace FsOrleansKafka
open System.Threading
open Orleans
open Microsoft.Extensions.Logging
open Orleans.Runtime
open Orleans.Services
open Microsoft.Extensions.Configuration

type IKafkaConsumer =
    inherit IGrainService

type KafkaConsumerService<'t,'u>(id:GrainId,silo:Silo,loggerFactory:ILoggerFactory,clstrClnt:IClusterClient,kcfg:IKafkaConfig<'t,'u>,cfg:IConfiguration) =
    inherit GrainService(id,silo,loggerFactory)
    let cts = new CancellationTokenSource()    

    let runGC() =
        async {
            while true do
                do! Async.Sleep (5*60000)
                System.GC.Collect()            
        }
        |> Async.Start

    //access streams lazily otherwise they don't seem be available at startup
    let streamProvider = lazy(clstrClnt.GetStreamProvider(kcfg.StreamProviderId)) 

    override this.StartInBackground() =       
        runGC()
        if kcfg.Simulate then 
            Async.Start(KafkaReader.readSim<'t,'u> streamProvider kcfg cts.Token,cts.Token)  //use simulator: can't read kafka with kerberos on windows 
        else
            Async.Start(KafkaReader.read<'t,'u> streamProvider kcfg cts.Token,cts.Token)
        base.StartInBackground()

    //override this.Start() =
    ////    Async.Start(Reader.read  grainFactory cts.Token,cts.Token)
    //    base.Start()
        

    override this.Stop() =
        cts.Cancel()
        base.Stop()

    interface IKafkaConsumer
            

        
