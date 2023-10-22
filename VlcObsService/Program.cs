using VlcObsService;
using VlcObsService.Vlc;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddWindowsService(options =>
        {
            options.ServiceName = "VLC-OBS Service";
        });
        services.AddHostedService<ObsWorker>();
        services.Configure<ObsWorkerOptions>(context.Configuration.GetSection("Obs"));
        services.AddSingleton<VlcInstanceManager>();
        services.AddHttpClient<VlcService>();
        services.Configure<VlcServiceOptions>(context.Configuration.GetSection("Vlc"));
    })
    .Build();

host.Run();

// If an error happened in the host, return a non-zero exit code
// This allows system (such as Windows Service Management or Linux equivalent)
// to leverage configured recovery options
if (ExitCode is not null)
    Environment.Exit(ExitCode.Value);

public partial class Program
{
    public static int? ExitCode { get; set; }
}