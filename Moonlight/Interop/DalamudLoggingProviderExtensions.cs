using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moonlight.MoonlightConfiguration;

namespace Moonlight.Interop;

public static class DalamudLoggingProviderExtensions
{
    public static ILoggingBuilder AddDalamudLogging(this ILoggingBuilder builder, IPluginLog pluginLog, bool hasModifiedGameFiles)
    {
        builder.ClearProviders();

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, DalamudLoggingProvider>
            (b => new DalamudLoggingProvider(b.GetRequiredService<MoonlightConfigService>(), pluginLog, hasModifiedGameFiles)));
        return builder;
    }
}