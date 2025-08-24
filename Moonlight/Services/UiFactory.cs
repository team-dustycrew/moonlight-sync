using Microsoft.Extensions.Logging;
using Moonlight.API.Dto.Group;
using Moonlight.PlayerData.Pairs;
using Moonlight.Services.Mediator;
using Moonlight.Services.ServerConfiguration;
using Moonlight.UI;
using Moonlight.UI.Components.Popup;
using Moonlight.WebAPI;

namespace Moonlight.Services;

public class UiFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly MoonlightMediator _moonlightMediator;
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverConfigManager;
    private readonly MoonlightProfileManager _moonlightProfileManager;
    private readonly PerformanceCollectorService _performanceCollectorService;

    public UiFactory(ILoggerFactory loggerFactory, MoonlightMediator moonlightMediator, ApiController apiController,
        UiSharedService uiSharedService, PairManager pairManager, ServerConfigurationManager serverConfigManager,
        MoonlightProfileManager moonlightProfileManager, PerformanceCollectorService performanceCollectorService)
    {
        _loggerFactory = loggerFactory;
        _moonlightMediator = moonlightMediator;
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _pairManager = pairManager;
        _serverConfigManager = serverConfigManager;
        _moonlightProfileManager = moonlightProfileManager;
        _performanceCollectorService = performanceCollectorService;
    }

    public SyncshellAdminUI CreateSyncshellAdminUi(GroupFullInfoDto dto)
    {
        return new SyncshellAdminUI(_loggerFactory.CreateLogger<SyncshellAdminUI>(), _moonlightMediator,
            _apiController, _uiSharedService, _pairManager, dto, _performanceCollectorService);
    }

    public StandaloneProfileUi CreateStandaloneProfileUi(Pair pair)
    {
        return new StandaloneProfileUi(_loggerFactory.CreateLogger<StandaloneProfileUi>(), _moonlightMediator,
            _uiSharedService, _serverConfigManager, _moonlightProfileManager, _pairManager, pair, _performanceCollectorService);
    }

    public PermissionWindowUI CreatePermissionPopupUi(Pair pair)
    {
        return new PermissionWindowUI(_loggerFactory.CreateLogger<PermissionWindowUI>(), pair,
            _moonlightMediator, _uiSharedService, _apiController, _performanceCollectorService);
    }
}
