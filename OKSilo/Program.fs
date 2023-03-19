namespace OKSilo
open System
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Orleans
open Orleans.Hosting
open Orleans.Configuration
open FsOrleansKafka
open OKGrains

///assembly attributes needed for Orleans to work in F#
module Load =

    [<assembly: Orleans.ApplicationPartAttribute("OKCodeGen")>]
    [<assembly: Orleans.ApplicationPartAttribute("Orleans.Core.Abstractions")>]
    [<assembly: Orleans.ApplicationPartAttribute("Orleans.Serialization")>]
    [<assembly: Orleans.ApplicationPartAttribute("Orleans.Core")>]
    [<assembly: Orleans.ApplicationPartAttribute("Orleans.Persistence.Memory")>]
    [<assembly: Orleans.ApplicationPartAttribute("Orleans.Runtime")>]
    [<assembly: Orleans.ApplicationPartAttribute("Orleans.Reminders")>]
    [<assembly: Orleans.ApplicationPartAttribute("OrleansDashboard.Core")>]
    [<assembly: Orleans.ApplicationPartAttribute("OrleansDashboard")>]
    [<assembly: Orleans.ApplicationPartAttribute("Orleans.Clustering.Redis")>]
    [<assembly: Orleans.ApplicationPartAttribute("Orleans.Persistence.Redis")>]
    [<assembly: Orleans.ApplicationPartAttribute("Orleans.Streaming")>]
    [<assembly: Orleans.ApplicationPartAttribute("Orleans.Serialization.Abstractions")>]
    [<assembly: Orleans.ApplicationPartAttribute("Orleans.Serialization")>]
    ()        

module Program =

    [<EntryPoint>]
    let main args =
        Host
            .CreateDefaultBuilder(args)
            .ConfigureServices(fun s -> s.AddSingleton<IKafkaConfig<KafkaInput,StreamData>,KafkaConfigProvider>() |> ignore)
            .UseOrleans(fun ctx sb -> 
                let isk8s = false //flag that should be set to true if the system is running under kubernetes
                sb                    
                    .AddMemoryStreams(Constants.STREAM_PROVIDER_NAME)
                    .UseInMemoryReminderService()
                    .UseDashboard()
                    .AddGrainService<FsOrleansKafka.KafkaConsumerService<KafkaInput,StreamData>>()
                    |> ignore

                if not isk8s then
                    sb
                            .UseLocalhostClustering()
                            .AddMemoryGrainStorage("PubSubStore")
                            |> ignore
                else
                    let redis = Environment.GetEnvironmentVariable("REDIS");
                    let redisConnectionString = $"{redis}:6379";
                    let serviceId = Environment.GetEnvironmentVariable("ORLEANS_SERVICE_ID");
                    let clusterId = Environment.GetEnvironmentVariable("ORLEANS_CLUSTER_ID");
                    let podIp = Environment.GetEnvironmentVariable("POD_IP");
                    let podName = Environment.GetEnvironmentVariable("POD_NAME");
                    sb
                        .Configure(fun (o:ClusterOptions) -> o.ClusterId <- clusterId; o.ServiceId <- serviceId)
                        .Configure(fun (o:EndpointOptions) -> o.AdvertisedIPAddress <- Net.IPAddress.Parse(podIp))
                        .Configure(fun (o:SiloOptions) -> o.SiloName <- podName)
                        //.UseKubernetesHosting()                                           //this did not work for our kubernetes cluster due to lack of permissions
                        .UseRedisClustering(redisConnectionString)
                        .AddRedisGrainStorage("PubSubStore", fun (options:Persistence.RedisStorageOptions) -> options.ConnectionString <- redisConnectionString)
                        |> ignore

                )
            .RunConsoleAsync()
            |> Async.AwaitTask
            |> Async.RunSynchronously
        0
