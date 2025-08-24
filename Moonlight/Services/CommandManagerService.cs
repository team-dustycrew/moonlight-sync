using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Moonlight.FileCache;
using Moonlight.MoonlightConfiguration;
using Moonlight.MoonlightConfiguration.Models;
using Moonlight.Services.Mediator;
using Moonlight.Services.ServerConfiguration;
using Moonlight.UI;
using Moonlight.WebAPI;
using System.Globalization;

namespace Moonlight.Services;

public sealed class CommandManagerService : IDisposable
{
    private const string _commandName = "/moonlight";

    private readonly ApiController _apiController;
    private readonly ICommandManager _commandManager;
    private readonly MoonlightMediator _mediator;
    private readonly MoonlightConfigService _moonlightConfigService;
    private readonly PerformanceCollectorService _performanceCollectorService;
    private readonly CacheMonitor _cacheMonitor;
    private readonly ServerConfigurationManager _serverConfigurationManager;

    public CommandManagerService(ICommandManager commandManager, PerformanceCollectorService performanceCollectorService,
        ServerConfigurationManager serverConfigurationManager, CacheMonitor periodicFileScanner,
        ApiController apiController, MoonlightMediator mediator, MoonlightConfigService moonlightConfigService)
    {
        _commandManager = commandManager;
        _performanceCollectorService = performanceCollectorService;
        _serverConfigurationManager = serverConfigurationManager;
        _cacheMonitor = periodicFileScanner;
        _apiController = apiController;
        _mediator = mediator;
        _moonlightConfigService = moonlightConfigService;
        _commandManager.AddHandler(_commandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Moonlight UI" + Environment.NewLine + Environment.NewLine +
                "Additionally possible commands:" + Environment.NewLine +
                "\t /moonlight toggle - Disconnects from moonlight, if connected. Connects to moonlight, if disconnected" + Environment.NewLine +
                "\t /moonlight toggle on|off - Connects or disconnects to moonlight respectively" + Environment.NewLine +
                "\t /moonlight gpose - Opens the moonlight Character Data Hub window" + Environment.NewLine +
                "\t /moonlight analyze - Opens the moonlight Character Data Analysis window" + Environment.NewLine +
                "\t /moonlight settings - Opens the moonlight Settings window"
        });
    }

    public void Dispose()
    {
        _commandManager.RemoveHandler(_commandName);
    }

    private void OnCommand(string command, string args)
    {
        var splitArgs = args.ToLowerInvariant().Trim().Split(" ", StringSplitOptions.RemoveEmptyEntries);

        if (splitArgs.Length == 0)
        {
            // Interpret this as toggling the UI
            if (_moonlightConfigService.Current.HasValidSetup())
                _mediator.Publish(new UiToggleMessage(typeof(CompactUi)));
            else
                _mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
            return;
        }

        if (!_moonlightConfigService.Current.HasValidSetup())
            return;

        if (string.Equals(splitArgs[0], "toggle", StringComparison.OrdinalIgnoreCase))
        {
            if (_apiController.ServerState == WebAPI.SignalR.Utils.ServerState.Disconnecting)
            {
                _mediator.Publish(new NotificationMessage("moonlight disconnecting", "Cannot use /toggle while Moonlight is still disconnecting",
                    NotificationType.Error));
            }

            if (_serverConfigurationManager.CurrentServer == null) return;
            var fullPause = splitArgs.Length > 1 ? splitArgs[1] switch
            {
                "on" => false,
                "off" => true,
                _ => !_serverConfigurationManager.CurrentServer.FullPause,
            } : !_serverConfigurationManager.CurrentServer.FullPause;

            if (fullPause != _serverConfigurationManager.CurrentServer.FullPause)
            {
                _serverConfigurationManager.CurrentServer.FullPause = fullPause;
                _serverConfigurationManager.Save();
                _ = _apiController.CreateConnectionsAsync();
            }
        }
        else if (string.Equals(splitArgs[0], "gpose", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(CharaDataHubUi)));
        }
        else if (string.Equals(splitArgs[0], "rescan", StringComparison.OrdinalIgnoreCase))
        {
            _cacheMonitor.InvokeScan();
        }
        else if (string.Equals(splitArgs[0], "perf", StringComparison.OrdinalIgnoreCase))
        {
            if (splitArgs.Length > 1 && int.TryParse(splitArgs[1], CultureInfo.InvariantCulture, out var limitBySeconds))
            {
                _performanceCollectorService.PrintPerformanceStats(limitBySeconds);
            }
            else
            {
                _performanceCollectorService.PrintPerformanceStats();
            }
        }
        else if (string.Equals(splitArgs[0], "medi", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.PrintSubscriberInfo();
        }
        else if (string.Equals(splitArgs[0], "analyze", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(DataAnalysisUi)));
        }
        else if (string.Equals(splitArgs[0], "settings", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
        }
    }
}