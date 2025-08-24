using Microsoft.Extensions.Logging;
using Moonlight.API.Data.Enum;
using Moonlight.PlayerData.Handlers;
using Moonlight.Services;
using Moonlight.Services.Mediator;

namespace Moonlight.PlayerData.Factories;

public class GameObjectHandlerFactory
{
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly MoonlightMediator _moonlightMediator;
    private readonly PerformanceCollectorService _performanceCollectorService;

    public GameObjectHandlerFactory(ILoggerFactory loggerFactory, PerformanceCollectorService performanceCollectorService, MoonlightMediator moonlightMediator,
        DalamudUtilService dalamudUtilService)
    {
        _loggerFactory = loggerFactory;
        _performanceCollectorService = performanceCollectorService;
        _moonlightMediator = moonlightMediator;
        _dalamudUtilService = dalamudUtilService;
    }

    public async Task<GameObjectHandler> Create(ObjectKind objectKind, Func<nint> getAddressFunc, bool isWatched = false)
    {
        return await _dalamudUtilService.RunOnFrameworkThread(() => new GameObjectHandler(_loggerFactory.CreateLogger<GameObjectHandler>(),
            _performanceCollectorService, _moonlightMediator, _dalamudUtilService, objectKind, getAddressFunc, isWatched)).ConfigureAwait(false);
    }
}