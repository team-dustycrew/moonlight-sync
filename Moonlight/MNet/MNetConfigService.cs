using Moonlight.MoonlightConfiguration;

namespace Moonlight.MNet;

public class MNetConfigService : ConfigurationServiceBase<MNetConfig>
{
    public const string ConfigName = "mnet.json";

    public MNetConfigService(string configDir) : base(configDir)
    {
    }

    public override string ConfigurationName => ConfigName;
}


