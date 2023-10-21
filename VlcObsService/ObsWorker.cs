using Microsoft.Extensions.Options;
using OBSWebsocketDotNet;
using VlcObsService.Vlc;

namespace VlcObsService
{
    public class ObsWorker : BackgroundService
    {
        private readonly ILogger<ObsWorker> _logger;
        private readonly OBSWebsocket obs = new ();
        private readonly VlcInstanceManager vlcWorker;
        private readonly IOptionsMonitor<ObsWorkerOptions> optionsMonitor;

        public ObsWorker(
            ILogger<ObsWorker> logger, 
            VlcInstanceManager vlcWorker,
            IOptionsMonitor<ObsWorkerOptions> optionsMonitor)
        {
            _logger = logger;
            obs.Connected += Obs_Connected;
            obs.Disconnected += Obs_Disconnected;
            obs.CurrentProgramSceneChanged += Obs_CurrentProgramSceneChanged;
            this.vlcWorker = vlcWorker;
            this.optionsMonitor = optionsMonitor;

        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!obs.IsConnected)
                {
                    var options = this.optionsMonitor.CurrentValue;
                    obs.ConnectAsync(options.Url, options.Password);
                    _logger.LogInformation("Waiting for OBS...");
                }
                await Task.Delay(obs.WSTimeout, stoppingToken);
            }
        }

        private void Obs_CurrentProgramSceneChanged(object? sender, OBSWebsocketDotNet.Types.Events.ProgramSceneChangedEventArgs e)
        {
            HandleScene(e.SceneName);
        }

        private void Obs_Disconnected(object? sender, OBSWebsocketDotNet.Communication.ObsDisconnectionInfo e)
        {
            _logger.LogInformation("Disconnected");
            _ = StopAndCloseAsync();
        }

        private async Task StopAndCloseAsync()
        {
            try
            {
                await vlcWorker.StopAsync();
            }
            finally
            {
                await vlcWorker.EnsureClosedAsync();
            }
        }

        private void Obs_Connected(object? sender, EventArgs e)
        {
            _logger.LogInformation("Connected");
            HandleScene(obs.GetCurrentProgramScene());
        }

        private void HandleScene(string scene)
        {
            _logger.LogInformation("Handling scene: {scene}", scene);
            if (optionsMonitor.CurrentValue.ScenesWithMusic.Contains(scene))
            {
                _ = vlcWorker.PlayAsync();
            }
            else
            {
                _ = vlcWorker.StopAsync();
            }
        }
    }
}