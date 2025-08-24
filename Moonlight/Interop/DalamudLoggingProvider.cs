﻿using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using Moonlight.MoonlightConfiguration;
using System.Collections.Concurrent;

namespace Moonlight.Interop;

[ProviderAlias("Dalamud")]
public sealed class DalamudLoggingProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, DalamudLogger> _loggers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly MoonlightConfigService _moonlightConfigService;
    private readonly IPluginLog _pluginLog;
    private readonly bool _hasModifiedGameFiles;

    public DalamudLoggingProvider(MoonlightConfigService moonlightConfigService, IPluginLog pluginLog, bool hasModifiedGameFiles)
    {
        _moonlightConfigService = moonlightConfigService;
        _pluginLog = pluginLog;
        _hasModifiedGameFiles = hasModifiedGameFiles;
    }

    public ILogger CreateLogger(string categoryName)
    {
        string catName = categoryName.Split(".", StringSplitOptions.RemoveEmptyEntries).Last();
        if (catName.Length > 15)
        {
            catName = string.Join("", catName.Take(6)) + "..." + string.Join("", catName.TakeLast(6));
        }
        else
        {
            catName = string.Join("", Enumerable.Range(0, 15 - catName.Length).Select(_ => " ")) + catName;
        }

        return _loggers.GetOrAdd(catName, name => new DalamudLogger(name, _moonlightConfigService, _pluginLog, _hasModifiedGameFiles));
    }

    public void Dispose()
    {
        _loggers.Clear();
        GC.SuppressFinalize(this);
    }
}