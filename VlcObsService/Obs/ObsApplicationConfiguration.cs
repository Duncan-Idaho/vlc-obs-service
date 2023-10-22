namespace VlcObsService.Obs;

public sealed class ObsApplicationConfiguration : IDisposable
{
    public IConfigurationRoot Configuration { get; }

    public ObsApplicationConfiguration(string? path)
    {
        var builder = new ConfigurationBuilder();
        if (path is not null)
            builder.AddIniFile(path, optional: true, reloadOnChange: true);
            
        Configuration = builder.Build();
    }

    public void Dispose()
    {
        if (Configuration is IDisposable disposableConfiguration)
            disposableConfiguration.Dispose();
    }
}
