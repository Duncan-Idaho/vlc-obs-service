using VlcObsService;
using VlcObsService.Vlc;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddHostedService<ObsWorker>();
        services.Configure<ObsWorkerOptions>(context.Configuration.GetSection("Obs"));
        services.AddSingleton<VlcInstanceManager>();
        services.AddHttpClient<VlcService>();
        services.Configure<VlcServiceOptions>(context.Configuration.GetSection("Vlc"));
    })
    .Build();

host.Run();