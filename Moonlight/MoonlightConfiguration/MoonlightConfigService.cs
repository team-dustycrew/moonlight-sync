using Moonlight.MoonlightConfiguration.Configurations;

namespace Moonlight.MoonlightConfiguration;

public class MoonlightConfigService : ConfigurationServiceBase<MoonlightConfig>
{
    public const string ConfigName = "config.json";

    public MoonlightConfigService(string configDir) : base(configDir)
    {
    }

    public override string ConfigurationName => ConfigName;
}