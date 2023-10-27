using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging.EventLog;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Mime;
using System.Runtime.InteropServices;
using VlcObsService;
using VlcObsService.Obs;
using VlcObsService.Vlc;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var obsConfiguration = new ObsApplicationConfiguration(GetObsConfiguration(context.Configuration));
        services.AddSingleton(_ => obsConfiguration); // Will be disposable
        services.Configure<ObsApplicationWebSocketOptions>(obsConfiguration.Configuration.GetSection("OBSWebSocket"));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            services.AddLogging(logging =>
            {
                Debug.Assert(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
                logging.AddEventLog();
            });
            services.Configure<EventLogSettings>(settings =>
            {
                Debug.Assert(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
                settings.SourceName = "VLC-OBS Service";
            });
        }

        services.AddHostedService<ObsWorker>();
        services.AddSingleton<ObsWatcher>();
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

static string? GetObsConfiguration(IConfiguration configuration)
{
    var obsConfigurationPath = configuration.GetValue<string?>("ObsConfigurationPath");
    return obsConfigurationPath is not null
        ? Path.Join(Environment.ExpandEnvironmentVariables(obsConfigurationPath), "global.ini")
        : null;
}

public partial class Program
{
    public static int? ExitCode { get; set; }
}