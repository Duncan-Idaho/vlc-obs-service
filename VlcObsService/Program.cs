using Microsoft.Extensions.DependencyInjection;
using VlcObsService;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddHostedService<ObsWorker>();
        services.Configure<ObsWorkerOptions>(context.Configuration.GetSection("Obs"));
    })
    .Build();

host.Run();
