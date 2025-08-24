using Moonlight.MoonlightConfiguration.Configurations;

namespace Moonlight.MoonlightConfiguration;

public static class ConfigurationExtensions
{
    public static bool HasValidSetup(this MoonlightConfig configuration)
    {
        return configuration.AcceptedAgreement && configuration.InitialScanComplete
                    && !string.IsNullOrEmpty(configuration.CacheFolder)
                    && Directory.Exists(configuration.CacheFolder);
    }
}