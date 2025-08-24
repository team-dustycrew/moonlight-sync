using Dalamud.Game.ClientState.Objects;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NReco.Logging.File;
using System.Net.Http.Headers;
using System.Reflection;
using Dalamud.Game;
using Moonlight.FileCache;
using Moonlight.Interop;
using Moonlight.Interop.Ipc;
using Moonlight.MoonlightConfiguration;
using Moonlight.MoonlightConfiguration.Configurations;
using Moonlight.PlayerData.Factories;
using Moonlight.PlayerData.Pairs;
using Moonlight.PlayerData.Services;
using Moonlight.Services;
using Moonlight.Services.CharaData;
using Moonlight.Services.Events;
using Moonlight.Services.Mediator;
using Moonlight.Services.ServerConfiguration;
using Moonlight.UI;
using Moonlight.UI.Components;
using Moonlight.UI.Components.Popup;
using Moonlight.UI.Handlers;
using Moonlight.WebAPI;
using Moonlight.WebAPI.Files;
using Moonlight.WebAPI.SignalR;

namespace Moonlight;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IHost _host;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commandManager, IDataManager gameData,
        IFramework framework, IObjectTable objectTable, IClientState clientState, ICondition condition, IChatGui chatGui,
        IGameGui gameGui, IDtrBar dtrBar, IPluginLog pluginLog, ITargetManager targetManager, INotificationManager notificationManager,
        ITextureProvider textureProvider, IContextMenu contextMenu, IGameInteropProvider gameInteropProvider, IGameConfig gameConfig,
        ISigScanner sigScanner)
    {
        if (!Directory.Exists(pluginInterface.ConfigDirectory.FullName))
            Directory.CreateDirectory(pluginInterface.ConfigDirectory.FullName);
        var traceDir = Path.Join(pluginInterface.ConfigDirectory.FullName, "tracelog");
        if (!Directory.Exists(traceDir))
            Directory.CreateDirectory(traceDir);

        foreach (var file in Directory.EnumerateFiles(traceDir)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc).Skip(9))
        {
            int attempts = 0;
            bool deleted = false;
            while (!deleted && attempts < 5)
            {
                try
                {
                    file.Delete();
                    deleted = true;
                }
                catch
                {
                    attempts++;
                    Thread.Sleep(500);
                }
            }
        }

        _host = new HostBuilder()
        .UseContentRoot(pluginInterface.ConfigDirectory.FullName)
        .ConfigureLogging(lb =>
        {
            lb.ClearProviders();
            lb.AddDalamudLogging(pluginLog, gameData.HasModifiedGameDataFiles);
            lb.AddFile(Path.Combine(traceDir, $"moonlight-trace-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.log"), (opt) =>
            {
                opt.Append = true;
                opt.RollingFilesConvention = FileLoggerOptions.FileRollingConvention.Ascending;
                opt.MinLevel = LogLevel.Trace;
                opt.FileSizeLimitBytes = 50 * 1024 * 1024;
            });
            lb.SetMinimumLevel(LogLevel.Trace);
        })
        .ConfigureServices(collection =>
        {
            collection.AddSingleton(new WindowSystem("Moonlight"));
            collection.AddSingleton<FileDialogManager>();
            collection.AddSingleton(new Dalamud.Localization("Moonlight.Localization.", "", useEmbedded: true));

            // add moonlight related singletons
            collection.AddSingleton<MoonlightMediator>();
            collection.AddSingleton<FileCacheManager>();
            collection.AddSingleton<ServerConfigurationManager>();
            collection.AddSingleton<ApiController>();
            collection.AddSingleton<PerformanceCollectorService>();
            collection.AddSingleton<HubFactory>();
            collection.AddSingleton<FileUploadManager>();
            collection.AddSingleton<FileTransferOrchestrator>();
            collection.AddSingleton<MoonlightPlugin>();
            collection.AddSingleton<MoonlightProfileManager>();
            collection.AddSingleton<GameObjectHandlerFactory>();
            collection.AddSingleton<FileDownloadManagerFactory>();
            collection.AddSingleton<PairHandlerFactory>();
            collection.AddSingleton<PairFactory>();
            collection.AddSingleton<XivDataAnalyzer>();
            collection.AddSingleton<CharacterAnalyzer>();
            collection.AddSingleton<TokenProvider>();
            collection.AddSingleton<PluginWarningNotificationService>();
            collection.AddSingleton<FileCompactor>();
            collection.AddSingleton<TagHandler>();
            collection.AddSingleton<IdDisplayHandler>();
            collection.AddSingleton<PlayerPerformanceService>();
            collection.AddSingleton<TransientResourceManager>();

            collection.AddSingleton<CharaDataManager>();
            collection.AddSingleton<CharaDataFileHandler>();
            collection.AddSingleton<CharaDataCharacterHandler>();
            collection.AddSingleton<CharaDataNearbyManager>();
            collection.AddSingleton<CharaDataGposeTogetherManager>();

            collection.AddSingleton(s => new VfxSpawnManager(s.GetRequiredService<ILogger<VfxSpawnManager>>(),
                gameInteropProvider, s.GetRequiredService<MoonlightMediator>()));
            collection.AddSingleton((s) => new BlockedCharacterHandler(s.GetRequiredService<ILogger<BlockedCharacterHandler>>(), gameInteropProvider));
            collection.AddSingleton((s) => new IpcProvider(s.GetRequiredService<ILogger<IpcProvider>>(),
                pluginInterface,
                s.GetRequiredService<CharaDataManager>(),
                s.GetRequiredService<MoonlightMediator>()));
            collection.AddSingleton<SelectPairForTagUi>();
            collection.AddSingleton((s) => new EventAggregator(pluginInterface.ConfigDirectory.FullName,
                s.GetRequiredService<ILogger<EventAggregator>>(), s.GetRequiredService<MoonlightMediator>()));
            collection.AddSingleton((s) => new DalamudUtilService(s.GetRequiredService<ILogger<DalamudUtilService>>(),
                clientState, objectTable, framework, gameGui, condition, gameData, targetManager, gameConfig,
                s.GetRequiredService<BlockedCharacterHandler>(), s.GetRequiredService<MoonlightMediator>(), s.GetRequiredService<PerformanceCollectorService>(),
                s.GetRequiredService<MoonlightConfigService>()));
            collection.AddSingleton((s) => new DtrEntry(s.GetRequiredService<ILogger<DtrEntry>>(), dtrBar, s.GetRequiredService<MoonlightConfigService>(),
                s.GetRequiredService<MoonlightMediator>(), s.GetRequiredService<PairManager>(), s.GetRequiredService<ApiController>()));
            collection.AddSingleton(s => new PairManager(s.GetRequiredService<ILogger<PairManager>>(), s.GetRequiredService<PairFactory>(),
                s.GetRequiredService<MoonlightConfigService>(), s.GetRequiredService<MoonlightMediator>(), contextMenu));
            collection.AddSingleton<RedrawManager>();
            collection.AddSingleton((s) => new IpcCallerPenumbra(s.GetRequiredService<ILogger<IpcCallerPenumbra>>(), pluginInterface,
                s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MoonlightMediator>(), s.GetRequiredService<RedrawManager>()));
            collection.AddSingleton((s) => new IpcCallerGlamourer(s.GetRequiredService<ILogger<IpcCallerGlamourer>>(), pluginInterface,
                s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MoonlightMediator>(), s.GetRequiredService<RedrawManager>()));
            collection.AddSingleton((s) => new IpcCallerCustomize(s.GetRequiredService<ILogger<IpcCallerCustomize>>(), pluginInterface,
                s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MoonlightMediator>()));
            collection.AddSingleton((s) => new IpcCallerHeels(s.GetRequiredService<ILogger<IpcCallerHeels>>(), pluginInterface,
                s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MoonlightMediator>()));
            collection.AddSingleton((s) => new IpcCallerHonorific(s.GetRequiredService<ILogger<IpcCallerHonorific>>(), pluginInterface,
                s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MoonlightMediator>()));
            collection.AddSingleton((s) => new IpcCallerMoodles(s.GetRequiredService<ILogger<IpcCallerMoodles>>(), pluginInterface,
                s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MoonlightMediator>()));
            collection.AddSingleton((s) => new IpcCallerPetNames(s.GetRequiredService<ILogger<IpcCallerPetNames>>(), pluginInterface,
                s.GetRequiredService<DalamudUtilService>(), s.GetRequiredService<MoonlightMediator>()));
            collection.AddSingleton((s) => new IpcCallerBrio(s.GetRequiredService<ILogger<IpcCallerBrio>>(), pluginInterface,
                s.GetRequiredService<DalamudUtilService>()));
            collection.AddSingleton((s) => new IpcManager(s.GetRequiredService<ILogger<IpcManager>>(),
                s.GetRequiredService<MoonlightMediator>(), s.GetRequiredService<IpcCallerPenumbra>(), s.GetRequiredService<IpcCallerGlamourer>(),
                s.GetRequiredService<IpcCallerCustomize>(), s.GetRequiredService<IpcCallerHeels>(), s.GetRequiredService<IpcCallerHonorific>(),
                s.GetRequiredService<IpcCallerMoodles>(), s.GetRequiredService<IpcCallerPetNames>(), s.GetRequiredService<IpcCallerBrio>()));
            collection.AddSingleton((s) => new NotificationService(s.GetRequiredService<ILogger<NotificationService>>(),
                s.GetRequiredService<MoonlightMediator>(), s.GetRequiredService<DalamudUtilService>(),
                notificationManager, chatGui, s.GetRequiredService<MoonlightConfigService>()));
            collection.AddSingleton((s) =>
            {
                var httpClient = new HttpClient();
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Moonlight", ver!.Major + "." + ver!.Minor + "." + ver!.Build));
                return httpClient;
            });
            collection.AddSingleton((s) => new MoonlightConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ServerConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new NotesConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ServerTagConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new TransientConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new XivDataStorageService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new PlayerPerformanceConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new CharaDataConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton<IConfigService<IMoonlightConfiguration>>(s => s.GetRequiredService<MoonlightConfigService>());
            collection.AddSingleton<IConfigService<IMoonlightConfiguration>>(s => s.GetRequiredService<ServerConfigService>());
            collection.AddSingleton<IConfigService<IMoonlightConfiguration>>(s => s.GetRequiredService<NotesConfigService>());
            collection.AddSingleton<IConfigService<IMoonlightConfiguration>>(s => s.GetRequiredService<ServerTagConfigService>());
            collection.AddSingleton<IConfigService<IMoonlightConfiguration>>(s => s.GetRequiredService<TransientConfigService>());
            collection.AddSingleton<IConfigService<IMoonlightConfiguration>>(s => s.GetRequiredService<XivDataStorageService>());
            collection.AddSingleton<IConfigService<IMoonlightConfiguration>>(s => s.GetRequiredService<PlayerPerformanceConfigService>());
            collection.AddSingleton<IConfigService<IMoonlightConfiguration>>(s => s.GetRequiredService<CharaDataConfigService>());
            collection.AddSingleton<ConfigurationMigrator>();
            collection.AddSingleton<ConfigurationSaveService>();

            collection.AddSingleton<HubFactory>();

            // add scoped services
            collection.AddScoped<DrawEntityFactory>();
            collection.AddScoped<CacheMonitor>();
            collection.AddScoped<UiFactory>();
            collection.AddScoped<SelectTagForPairUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, SettingsUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, CompactUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, IntroUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, DownloadUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, PopoutProfileUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, DataAnalysisUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, JoinSyncshellUI>();
            collection.AddScoped<WindowMediatorSubscriberBase, CreateSyncshellUI>();
            collection.AddScoped<WindowMediatorSubscriberBase, EventViewerUI>();
            collection.AddScoped<WindowMediatorSubscriberBase, CharaDataHubUi>();

            collection.AddScoped<WindowMediatorSubscriberBase, EditProfileUi>((s) => new EditProfileUi(s.GetRequiredService<ILogger<EditProfileUi>>(),
                s.GetRequiredService<MoonlightMediator>(), s.GetRequiredService<ApiController>(), s.GetRequiredService<UiSharedService>(), s.GetRequiredService<FileDialogManager>(),
                s.GetRequiredService<MoonlightProfileManager>(), s.GetRequiredService<PerformanceCollectorService>()));
            collection.AddScoped<WindowMediatorSubscriberBase, PopupHandler>();
            collection.AddScoped<IPopupHandler, BanUserPopupHandler>();
            collection.AddScoped<IPopupHandler, CensusPopupHandler>();
            collection.AddScoped<CacheCreationService>();
            collection.AddScoped<PlayerDataFactory>();
            collection.AddScoped<VisibleUserDataDistributor>();
            collection.AddScoped((s) => new UiService(s.GetRequiredService<ILogger<UiService>>(), pluginInterface.UiBuilder, s.GetRequiredService<MoonlightConfigService>(),
                s.GetRequiredService<WindowSystem>(), s.GetServices<WindowMediatorSubscriberBase>(),
                s.GetRequiredService<UiFactory>(),
                s.GetRequiredService<FileDialogManager>(), s.GetRequiredService<MoonlightMediator>()));
            collection.AddScoped((s) => new CommandManagerService(commandManager, s.GetRequiredService<PerformanceCollectorService>(),
                s.GetRequiredService<ServerConfigurationManager>(), s.GetRequiredService<CacheMonitor>(), s.GetRequiredService<ApiController>(),
                s.GetRequiredService<MoonlightMediator>(), s.GetRequiredService<MoonlightConfigService>()));
            collection.AddScoped((s) => new UiSharedService(s.GetRequiredService<ILogger<UiSharedService>>(), s.GetRequiredService<IpcManager>(), s.GetRequiredService<ApiController>(),
                s.GetRequiredService<CacheMonitor>(), s.GetRequiredService<FileDialogManager>(), s.GetRequiredService<MoonlightConfigService>(), s.GetRequiredService<DalamudUtilService>(),
                pluginInterface, textureProvider, s.GetRequiredService<Dalamud.Localization>(), s.GetRequiredService<ServerConfigurationManager>(), s.GetRequiredService<TokenProvider>(),
                s.GetRequiredService<MoonlightMediator>()));

            collection.AddHostedService(p => p.GetRequiredService<ConfigurationSaveService>());
            collection.AddHostedService(p => p.GetRequiredService<MoonlightMediator>());
            collection.AddHostedService(p => p.GetRequiredService<NotificationService>());
            collection.AddHostedService(p => p.GetRequiredService<FileCacheManager>());
            collection.AddHostedService(p => p.GetRequiredService<ConfigurationMigrator>());
            collection.AddHostedService(p => p.GetRequiredService<DalamudUtilService>());
            collection.AddHostedService(p => p.GetRequiredService<PerformanceCollectorService>());
            collection.AddHostedService(p => p.GetRequiredService<DtrEntry>());
            collection.AddHostedService(p => p.GetRequiredService<EventAggregator>());
            collection.AddHostedService(p => p.GetRequiredService<IpcProvider>());
            collection.AddHostedService(p => p.GetRequiredService<MoonlightPlugin>());
        })
        .Build();

        _ = _host.StartAsync();
    }

    public void Dispose()
    {
        _host.StopAsync().GetAwaiter().GetResult();
        _host.Dispose();
    }
}