using Microsoft.Extensions.Logging;
using MoonLight.API.Dto.User;
using Moonlight.PlayerData.Pairs;
using Moonlight.Services.Mediator;
using Moonlight.Services.ServerConfiguration;

namespace Moonlight.PlayerData.Factories;

public class PairFactory
{
    private readonly PairHandlerFactory _cachedPlayerFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly MoonlightMediator _moonlightMediator;
    private readonly ServerConfigurationManager _serverConfigurationManager;

    public PairFactory(ILoggerFactory loggerFactory, PairHandlerFactory cachedPlayerFactory,
        MoonlightMediator moonlightMediator, ServerConfigurationManager serverConfigurationManager)
    {
        _loggerFactory = loggerFactory;
        _cachedPlayerFactory = cachedPlayerFactory;
        _moonlightMediator = moonlightMediator;
        _serverConfigurationManager = serverConfigurationManager;
    }

    public Pair Create(UserFullPairDto userPairDto)
    {
        return new Pair(_loggerFactory.CreateLogger<Pair>(), userPairDto, _cachedPlayerFactory, _moonlightMediator, _serverConfigurationManager);
    }

    public Pair Create(UserPairDto userPairDto)
    {
        return new Pair(_loggerFactory.CreateLogger<Pair>(), new(userPairDto.User, userPairDto.IndividualPairStatus, [], userPairDto.OwnPermissions, userPairDto.OtherPermissions),
            _cachedPlayerFactory, _moonlightMediator, _serverConfigurationManager);
    }
}