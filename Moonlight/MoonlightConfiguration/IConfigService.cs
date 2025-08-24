using Moonlight.MoonlightConfiguration.Configurations;

namespace Moonlight.MoonlightConfiguration;

public interface IConfigService<out T> : IDisposable where T : IMoonlightConfiguration
{
    T Current { get; }
    string ConfigurationName { get; }
    string ConfigurationPath { get; }
    public event EventHandler? ConfigSave;
    void UpdateLastWriteTime();
}
