using System.Reflection;

using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

using Dalamud.Utility;

using MoonLight.API.Dto;
using MoonLight.API.Dto.User;
using MoonLight.API.Data;
using MoonLight.API.Data.Extensions;
using MoonLight.API.SignalR;
using Moonlight.MoonlightConfiguration;
using Moonlight.MoonlightConfiguration.Models;
using Moonlight.PlayerData.Pairs;
using Moonlight.Services;
using Moonlight.Services.Mediator;
using Moonlight.Services.ServerConfiguration;
using Moonlight.WebAPI.SignalR;
using Moonlight.WebAPI.SignalR.Utils;


namespace Moonlight.WebAPI;

#pragma warning disable MA0040
/// <summary>
/// Main controller for managing API connections to the Moonlight server.
/// Handles authentication, connection management, and communication with the Moonlight hub.
/// </summary>
public sealed partial class ApiController : DisposableMediatorSubscriberBase, IMoonLightHubClient
{
    /// <summary>
    /// The name of the main Moonlight server
    /// </summary>
    public const string MainServer = "Moonlight Server 1";

    /// <summary>
    /// The WebSocket URI for the main Moonlight service
    /// </summary>
    /// <summary>
    /// The WebSocket URI for the main Moonlight service (test environment)
    /// </summary>
    public const string MainServiceUri = "wss://maretestapp-hvt4e.sevalla.app";

    // Service dependencies injected via constructor
    /// <summary>
    /// Service for interacting with Dalamud and FFXIV game data
    /// </summary>
    private readonly DalamudUtilService _dalamudUtil;

    /// <summary>
    /// Factory for creating and managing SignalR hub connections
    /// </summary>
    private readonly HubFactory _hubFactory;

    /// <summary>
    /// Manager for handling user pairs and groups
    /// </summary>
    private readonly PairManager _pairManager;

    /// <summary>
    /// Manager for server configuration settings
    /// </summary>
    private readonly ServerConfigurationManager _serverManager;

    /// <summary>
    /// Provider for managing JWT authentication tokens
    /// </summary>
    private readonly TokenProvider _tokenProvider;

    /// <summary>
    /// Service for managing Moonlight-specific configuration
    /// </summary>
    private readonly MoonlightConfigService _moonlightConfigService;

    // Connection state management
    /// <summary>
    /// Cancellation token source for managing connection lifecycle
    /// </summary>
    private CancellationTokenSource _connectionCancellationTokenSource;

    /// <summary>
    /// Data transfer object containing connection information from the server
    /// </summary>
    private ConnectionDto? _connectionDto;

    /// <summary>
    /// Flag to suppress notifications on the next info update
    /// </summary>
    private bool _doNotNotifyOnNextInfo = false;

    /// <summary>
    /// Cancellation token source for health check operations
    /// </summary>
    private CancellationTokenSource? _healthCheckTokenSource = new();

    /// <summary>
    /// Flag indicating whether the API hooks have been initialized
    /// </summary>
    private bool _initialized;

    /// <summary>
    /// The last authentication token used for the connection
    /// </summary>
    private string? _lastUsedToken;

    /// <summary>
    /// The SignalR hub connection to the Moonlight server
    /// </summary>
    private HubConnection? _moonlightHub;

    /// <summary>
    /// Current state of the server connection
    /// </summary>
    private ServerState _serverState;

    /// <summary>
    /// The last received census update message containing character data
    /// </summary>
    private CensusUpdateMessage? _lastCensus;

    /// <summary>
    /// Initializes a new instance of the ApiController with all required dependencies.
    /// Sets up mediator subscriptions for various events and initializes connection state.
    /// </summary>
    public ApiController(ILogger<ApiController> logger, HubFactory hubFactory, DalamudUtilService dalamudUtil, PairManager pairManager, ServerConfigurationManager serverManager, MoonlightMediator mediator, TokenProvider tokenProvider, MoonlightConfigService moonlightConfigService) : base(logger, mediator)
    {
        _hubFactory = hubFactory;
        _dalamudUtil = dalamudUtil;
        _pairManager = pairManager;
        _serverManager = serverManager;
        _tokenProvider = tokenProvider;
        _moonlightConfigService = moonlightConfigService;
        _connectionCancellationTokenSource = new CancellationTokenSource();

        // Subscribe to various events through the mediator pattern
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) => DalamudUtilOnLogIn());
        Mediator.Subscribe<DalamudLogoutMessage>(this, (_) => DalamudUtilOnLogOut());
        Mediator.Subscribe<HubClosedMessage>(this, (msg) => MoonlightHubOnClosed(msg.Exception));
        Mediator.Subscribe<HubReconnectedMessage>(this, (msg) => _ = MoonlightHubOnReconnectedAsync());
        Mediator.Subscribe<HubReconnectingMessage>(this, (msg) => MoonlightHubOnReconnecting(msg.Exception));
        Mediator.Subscribe<CyclePauseMessage>(this, (msg) => _ = CyclePauseAsync(msg.UserData));
        Mediator.Subscribe<CensusUpdateMessage>(this, (msg) => _lastCensus = msg);
        Mediator.Subscribe<PauseMessage>(this, (msg) => _ = PauseAsync(msg.UserData));

        // Initialize with offline state
        ServerState = ServerState.Offline;

        // Auto-login if Dalamud is already logged in
        if (_dalamudUtil.IsLoggedIn)
        {
            DalamudUtilOnLogIn();
        }
    }

    /// <summary>
    /// Gets the authentication failure message when connection fails due to auth issues
    /// </summary>
    public string AuthFailureMessage { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the current client version from the server connection, or 0.0.0 if not connected
    /// </summary>
    public Version CurrentClientVersion => _connectionDto?.CurrentClientVersion ?? new Version(0, 0, 0);

    /// <summary>
    /// Gets the default permissions from the server, or null if not connected
    /// </summary>
    public DefaultPermissionsDto? DefaultPermissions => _connectionDto?.DefaultPreferredPermissions ?? null;

    /// <summary>
    /// Gets the display name for the current user (alias or UID)
    /// </summary>
    public string DisplayName => _connectionDto?.User.AliasOrUID ?? string.Empty;

    /// <summary>
    /// Gets whether the client is currently connected to the server
    /// </summary>
    public bool IsConnected => ServerState == ServerState.Connected;

    /// <summary>
    /// Gets whether the current client version is compatible with the server
    /// </summary>
    public bool IsCurrentVersion => (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0)) >= (_connectionDto?.CurrentClientVersion ?? new Version(0, 0, 0, 0));

    /// <summary>
    /// Gets the number of online users from system info
    /// </summary>
    public int OnlineUsers => SystemInfoDto.OnlineUsers;

    /// <summary>
    /// Gets whether the server is alive and reachable (connected, rate limited, unauthorized, or disconnected but responsive)
    /// </summary>
    public bool ServerAlive => ServerState is ServerState.Connected or ServerState.RateLimited or ServerState.Unauthorized or ServerState.Disconnected;

    /// <summary>
    /// Gets server information from the connection, or empty info if not connected
    /// </summary>
    public ServerInfo ServerInfo => _connectionDto?.ServerInfo ?? new ServerInfo();

    /// <summary>
    /// Gets or sets the current server connection state with logging
    /// </summary>
    public ServerState ServerState
    {
        get => _serverState;
        private set
        {
            Logger.LogDebug("New ServerState: {value}, prev ServerState: {_serverState}", value, _serverState);
            _serverState = value;
        }
    }

    /// <summary>
    /// Gets system information from the server
    /// </summary>
    public SystemInfoDto SystemInfoDto { get; private set; } = new();

    /// <summary>
    /// Gets the unique identifier for the current user, or empty string if not connected
    /// </summary>
    public string? PublicUserID
    {
        get => _connectionDto?.User.publicUserID ?? string.Empty;
    }

    /// <summary>
    /// Performs a health check on the client connection to ensure it's still valid
    /// </summary>
    /// <returns>True if the client is healthy, false otherwise</returns>
    public async Task<bool> CheckClientHealth()
    {
        if (_moonlightHub == null || _moonlightHub.State != HubConnectionState.Connected)
        {
            Logger.LogWarning("Client health check failed, moonlight hub is null or not connected");
            return false;
        }

        return await _moonlightHub.InvokeAsync<bool>(nameof(CheckClientHealth)).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates and establishes connections to the Moonlight server.
    /// Handles authentication, configuration validation, and connection state management.
    /// </summary>
    public async Task CreateConnectionsAsync()
    {
        // Ensure census popup has been shown before proceeding
        if (_serverManager.ShownCensusPopup == false)
        {
            Mediator.Publish(new OpenCensusPopupMessage());
            while (_serverManager.ShownCensusPopup == false)
            {
                await Task.Delay(500).ConfigureAwait(false);
            }
        }

        Logger.LogDebug("CreateConnections called");

        // Check if the server is fully paused
        if (_serverManager.CurrentServer?.FullPause ?? true)
        {
            Logger.LogInformation("Not recreating Connection, paused");
            _connectionDto = null;
            await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(false);
            _connectionCancellationTokenSource?.Cancel();
            return;
        }

        // Stop any existing connection before creating a new one
        await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(false);

        Logger.LogInformation("Recreating Connection");
        Mediator.Publish(new EventMessage(new Services.Events.Event(nameof(ApiController), Services.Events.EventSeverity.Informational, $"Starting Connection to {_serverManager.CurrentServer.ServerName}")));

        // Create new cancellation token for this connection attempt
        _connectionCancellationTokenSource?.Cancel();
        _connectionCancellationTokenSource?.Dispose();
        _connectionCancellationTokenSource = new CancellationTokenSource();
        var token = _connectionCancellationTokenSource.Token;

        // Connection retry loop
        while (ServerState is not ServerState.Connected && token.IsCancellationRequested == false)
        {
            AuthFailureMessage = string.Empty;

            await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(false);
            ServerState = ServerState.Connecting;

            try
            {
                Logger.LogDebug("Building connection");

                // Wait for player to be loaded in FFXIV
                while (await _dalamudUtil.GetIsPlayerPresentAsync().ConfigureAwait(false) == false && token.IsCancellationRequested == false)
                {
                    Logger.LogDebug("Player not loaded in yet, waiting");
                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                }

                if (token.IsCancellationRequested) break;

                // Create and configure the SignalR hub connection
                _moonlightHub = _hubFactory.GetOrCreate(token);
                InitializeApiHooks();

                // Start the connection
                await _moonlightHub.StartAsync(token).ConfigureAwait(false);

                // Get connection data from server
                _connectionDto = await GetConnectionDto().ConfigureAwait(false);

                ServerState = ServerState.Connected;

                var currentClientVer = Assembly.GetExecutingAssembly().GetName().Version!;

                // Version compatibility checks
                if (_connectionDto.ServerVersion != IMoonLightHub.ApiVersion)
                {
                    if (_connectionDto.CurrentClientVersion > currentClientVer)
                    {
                        Mediator.Publish(new NotificationMessage("Client incompatible",
                            $"Your client is outdated ({currentClientVer.Major}.{currentClientVer.Minor}.{currentClientVer.Build}), current is: " +
                            $"{_connectionDto.CurrentClientVersion.Major}.{_connectionDto.CurrentClientVersion.Minor}.{_connectionDto.CurrentClientVersion.Build}. " +
                            $"This client version is incompatible and will not be able to connect. Please update your Moonlight client.",
                            NotificationType.Error));
                    }
                    await StopConnectionAsync(ServerState.VersionMisMatch).ConfigureAwait(false);
                    return;
                }

                // Warn about outdated client
                if (_connectionDto.CurrentClientVersion > currentClientVer)
                {
                    Mediator.Publish(new NotificationMessage("Client outdated",
                        $"Your client is outdated ({currentClientVer.Major}.{currentClientVer.Minor}.{currentClientVer.Build}), current is: " +
                        $"{_connectionDto.CurrentClientVersion.Major}.{_connectionDto.CurrentClientVersion.Minor}.{_connectionDto.CurrentClientVersion.Build}. " +
                        $"Please keep your Moonlight client up-to-date.",
                        NotificationType.Warning));
                }

                // Check for modified game files
                if (_dalamudUtil.HasModifiedGameFiles)
                {
                    Logger.LogError("Detected modified game files on connection");
                    if (!_moonlightConfigService.Current.DebugStopWhining)
                        Mediator.Publish(new NotificationMessage("Modified Game Files detected",
                            "Dalamud is reporting your FFXIV installation has modified game files. Any mods installed through TexTools will produce this message. " +
                            "Moonlight, Penumbra, and some other plugins assume your FFXIV installation is unmodified in order to work. " +
                            "Synchronization with pairs/shells can break because of this. Exit the game, open XIVLauncher, click the arrow next to Log In " +
                            "and select 'repair game files' to resolve this issue. Afterwards, do not install any mods with TexTools. Your plugin configurations will remain, as will mods enabled in Penumbra.",
                            NotificationType.Error, TimeSpan.FromSeconds(15)));
                }

                // Check for LOD settings that may cause issues
                if (_dalamudUtil.IsLodEnabled && !_naggedAboutLod)
                {
                    _naggedAboutLod = true;
                    Logger.LogWarning("Model LOD is enabled during connection");
                    if (!_moonlightConfigService.Current.DebugStopWhining)
                    {
                        Mediator.Publish(new NotificationMessage("Model LOD is enabled",
                            "You have \"Use low-detail models on distant objects (LOD)\" enabled. Having model LOD enabled is known to be a reason to cause " +
                            "random crashes when loading in or rendering modded pairs. Disabling LOD has a very low performance impact. Disable LOD while using Moonlight: " +
                            "Go to XIV Menu -> System Configuration -> Graphics Settings and disable the model LOD option.", NotificationType.Warning, TimeSpan.FromSeconds(15)));
                    }
                }

                // Reset LOD nag flag if LOD was disabled
                if (_naggedAboutLod && _dalamudUtil.IsLodEnabled == false)
                {
                    _naggedAboutLod = false;
                }

                // Load initial data
                await LoadIninitialPairsAsync().ConfigureAwait(false);
                await LoadOnlinePairsAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Logger.LogWarning("Connection attempt cancelled");
                return;
            }
            catch (HttpRequestException ex)
            {
                Logger.LogWarning(ex, "HttpRequestException on Connection");

                // Handle authorization failures
                if (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    await StopConnectionAsync(ServerState.Unauthorized).ConfigureAwait(false);
                    return;
                }

                // Retry with backoff for other HTTP errors
                ServerState = ServerState.Reconnecting;
                Logger.LogInformation("Failed to establish connection, retrying");
                await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 20)), token).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                Logger.LogWarning(ex, "InvalidOperationException on connection");
                await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Exception on Connection");

                // Retry with backoff for unexpected errors
                Logger.LogInformation("Failed to establish connection, retrying");
                await Task.Delay(TimeSpan.FromSeconds(new Random().Next(5, 20)), token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Flag to track whether we've already nagged the user about LOD being enabled
    /// </summary>
    private bool _naggedAboutLod = false;

    /// <summary>
    /// Temporarily pauses and then unpauses a user pair's permissions.
    /// Used for cycling pause state to refresh synchronization.
    /// </summary>
    /// <param name="userData">The user data for the pair to cycle pause</param>
    public Task CyclePauseAsync(UserData userData)
    {
        CancellationTokenSource cts = new();
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        _ = Task.Run(async () =>
        {
            // Find the pair for this user
            var pair = _pairManager.GetOnlineUserPairs().Single(p => p.UserPair != null && p.UserData == userData);
            var perm = pair.UserPair!.OwnPermissions;

            // Set to paused
            perm.SetPaused(paused: true);
            await UserSetPairPermissions(new UserPermissionsDto(userData, perm)).ConfigureAwait(false);

            // Wait until the change is applied
            while (pair.UserPair!.OwnPermissions != perm)
            {
                await Task.Delay(250, cts.Token).ConfigureAwait(false);
                Logger.LogTrace("Waiting for permissions change for {data}", userData);
            }

            // Set to unpaused
            perm.SetPaused(paused: false);
            await UserSetPairPermissions(new UserPermissionsDto(userData, perm)).ConfigureAwait(false);
        }, cts.Token).ContinueWith((t) => cts.Dispose());

        return Task.CompletedTask;
    }

    /// <summary>
    /// Pauses a user pair's permissions.
    /// </summary>
    /// <param name="userData">The user data for the pair to pause</param>
    public async Task PauseAsync(UserData userData)
    {
        var pair = _pairManager.GetOnlineUserPairs().Single(p => p.UserPair != null && p.UserData == userData);
        var perm = pair.UserPair!.OwnPermissions;
        perm.SetPaused(paused: true);
        await UserSetPairPermissions(new UserPermissionsDto(userData, perm)).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the connection data transfer object from the server
    /// </summary>
    /// <returns>The connection DTO containing server and user information</returns>
    public Task<ConnectionDto> GetConnectionDto() => GetConnectionDtoAsync(true);

    /// <summary>
    /// Gets the connection data transfer object from the server with option to publish connected event
    /// </summary>
    /// <param name="publishConnected">Whether to publish a connected message via mediator</param>
    /// <returns>The connection DTO containing server and user information</returns>
    public async Task<ConnectionDto> GetConnectionDtoAsync(bool publishConnected)
    {
        var dto = await _moonlightHub!.InvokeAsync<ConnectionDto>(nameof(GetConnectionDto)).ConfigureAwait(false);
        if (publishConnected) Mediator.Publish(new ConnectedMessage(dto));
        return dto;
    }

    /// <summary>
    /// Disposes of the ApiController, stopping connections and cancelling tokens
    /// </summary>
    /// <param name="disposing">Whether we're disposing managed resources</param>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _healthCheckTokenSource?.Cancel();
        _ = Task.Run(async () => await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(false));
        _connectionCancellationTokenSource?.Cancel();
    }

    /// <summary>
    /// Performs periodic health checks on the client connection to ensure it remains valid
    /// </summary>
    /// <param name="ct">Cancellation token to stop health checks</param>
    private async Task ClientHealthCheckAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _moonlightHub != null)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            Logger.LogDebug("Checking Client Health State");

            // Token refresh logic is commented out
            // bool requireReconnect = await RefreshTokenAsync(ct).ConfigureAwait(false);
            //if (requireReconnect) break;

            // Perform health check
            try
            {
                var ok = await CheckClientHealth().ConfigureAwait(false);
                if (!ok)
                {
                    Logger.LogWarning("Health check reported not connected; skipping stop");
                    continue;
                }
            }
            catch (HubException ex) when (ex.Message?.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AuthFailureMessage = "Unauthorized";
                await StopConnectionAsync(ServerState.Unauthorized).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Health check failed");
            }
        }
    }

    /// <summary>
    /// Handles Dalamud login events by checking auto-login settings and connecting if enabled
    /// </summary>
    private void DalamudUtilOnLogIn()
    {
        var charaName = _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult();
        var worldId = _dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult();
        var auth = _serverManager.CurrentServer.Authentications.Find(f => string.Equals(f.CharacterName, charaName, StringComparison.Ordinal) && f.WorldId == worldId);

        if (auth?.AutoLogin ?? false)
        {
            Logger.LogInformation("Logging into {chara}", charaName);
            _ = Task.Run(CreateConnectionsAsync);
        }
        else
        {
            Logger.LogInformation("Not logging into {chara}, auto login disabled", charaName);
            _ = Task.Run(async () => await StopConnectionAsync(ServerState.NoAutoLogon).ConfigureAwait(false));
        }
    }

    /// <summary>
    /// Handles Dalamud logout events by stopping connections and setting state to offline
    /// </summary>
    private void DalamudUtilOnLogOut()
    {
        _ = Task.Run(async () => await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(false));
        ServerState = ServerState.Offline;
    }

    /// <summary>
    /// Initializes API hooks and event handlers for the SignalR hub connection
    /// </summary>
    private void InitializeApiHooks()
    {
        if (_moonlightHub == null) return;

        Logger.LogDebug("Initializing data");

        // Set up event handlers for various server messages
        OnDownloadReady((guid) => _ = Client_DownloadReady(guid));
        OnReceiveServerMessage((sev, msg) => _ = Client_ReceiveServerMessage(sev, msg));
        OnUpdateSystemInfo((dto) => _ = Client_UpdateSystemInfo(dto));

        // User-related event handlers
        OnUserSendOffline((dto) => _ = Client_UserSendOffline(dto));
        OnUserAddClientPair((dto) => _ = Client_UserAddClientPair(dto));
        OnUserReceiveCharacterData((dto) => _ = Client_UserReceiveCharacterData(dto));
        OnUserRemoveClientPair(dto => _ = Client_UserRemoveClientPair(dto));
        OnUserSendOnline(dto => _ = Client_UserSendOnline(dto));
        OnUserUpdateOtherPairPermissions(dto => _ = Client_UserUpdateOtherPairPermissions(dto));
        OnUserUpdateSelfPairPermissions(dto => _ = Client_UserUpdateSelfPairPermissions(dto));
        OnUserReceiveUploadStatus(dto => _ = Client_UserReceiveUploadStatus(dto));
        OnUserUpdateProfile(dto => _ = Client_UserUpdateProfile(dto));
        OnUserDefaultPermissionUpdate(dto => _ = Client_UserUpdateDefaultPermissions(dto));
        OnUpdateUserIndividualPairStatusDto(dto => _ = Client_UpdateUserIndividualPairStatusDto(dto));

        // Group-related event handlers
        OnGroupChangePermissions((dto) => _ = Client_GroupChangePermissions(dto));
        OnGroupDelete((dto) => _ = Client_GroupDelete(dto));
        OnGroupPairChangeUserInfo((dto) => _ = Client_GroupPairChangeUserInfo(dto));
        OnGroupPairJoined((dto) => _ = Client_GroupPairJoined(dto));
        OnGroupPairLeft((dto) => _ = Client_GroupPairLeft(dto));
        OnGroupSendFullInfo((dto) => _ = Client_GroupSendFullInfo(dto));
        OnGroupSendInfo((dto) => _ = Client_GroupSendInfo(dto));
        OnGroupChangeUserPairPermissions((dto) => _ = Client_GroupChangeUserPairPermissions(dto));

        // GPose lobby event handlers
        OnGposeLobbyJoin((dto) => _ = Client_GposeLobbyJoin(dto));
        OnGposeLobbyLeave((dto) => _ = Client_GposeLobbyLeave(dto));
        OnGposeLobbyPushCharacterData((dto) => _ = Client_GposeLobbyPushCharacterData(dto));
        OnGposeLobbyPushPoseData((dto, data) => _ = Client_GposeLobbyPushPoseData(dto, data));
        OnGposeLobbyPushWorldData((dto, data) => _ = Client_GposeLobbyPushWorldData(dto, data));

        // Start health check monitoring
        _healthCheckTokenSource?.Cancel();
        _healthCheckTokenSource?.Dispose();
        _healthCheckTokenSource = new CancellationTokenSource();
        _ = ClientHealthCheckAsync(_healthCheckTokenSource.Token).ContinueWith(t => Logger.LogError(t.Exception, "Health check task faulted"), TaskContinuationOptions.OnlyOnFaulted);

        _initialized = true;
    }

    /// <summary>
    /// Loads initial pair data from the server including groups and individual pairs
    /// </summary>
    private async Task LoadIninitialPairsAsync()
    {
        // Load all groups
        foreach (var entry in await GroupsGetAll().ConfigureAwait(false))
        {
            Logger.LogDebug("Group: {entry}", entry);
            _pairManager.AddGroup(entry);
        }

        // Load all paired clients
        foreach (var userPair in await UserGetPairedClients().ConfigureAwait(false))
        {
            Logger.LogDebug("Individual Pair: {userPair}", userPair);
            _pairManager.AddUserPair(userPair);
        }
    }

    /// <summary>
    /// Loads currently online pairs from the server, optionally including census data
    /// </summary>
    private async Task LoadOnlinePairsAsync()
    {
        CensusDataDto? dto = null;

        // Include census data if enabled and available
        if (_serverManager.SendCensusData && _lastCensus != null)
        {
            var world = await _dalamudUtil.GetWorldIdAsync().ConfigureAwait(false);
            dto = new((ushort)world, _lastCensus.RaceId, _lastCensus.TribeId, _lastCensus.Gender);
            Logger.LogDebug("Attaching Census Data: {data}", dto);
        }

        // Mark all online pairs as online
        foreach (var entry in await UserGetOnlinePairs(dto).ConfigureAwait(false))
        {
            Logger.LogDebug("Pair online: {pair}", entry);
            _pairManager.MarkPairOnline(entry, sendNotif: false);
        }
    }

    /// <summary>
    /// Handles hub connection closed events
    /// </summary>
    /// <param name="arg">Exception that caused the closure, if any</param>
    private void MoonlightHubOnClosed(Exception? arg)
    {
        _healthCheckTokenSource?.Cancel();
        Mediator.Publish(new DisconnectedMessage());
        ServerState = ServerState.Offline;

        if (arg != null)
        {
            Logger.LogWarning(arg, "Connection closed");
        }
        else
        {
            Logger.LogInformation("Connection closed");
        }
    }

    /// <summary>
    /// Handles hub reconnection events by re-initializing the connection and reloading data
    /// </summary>
    private async Task MoonlightHubOnReconnectedAsync()
    {
        ServerState = ServerState.Reconnecting;
        try
        {
            // Re-initialize hooks and get connection data
            InitializeApiHooks();
            _connectionDto = await GetConnectionDtoAsync(publishConnected: false).ConfigureAwait(false);

            // Check version compatibility
            if (_connectionDto.ServerVersion != IMoonLightHub.ApiVersion)
            {
                await StopConnectionAsync(ServerState.VersionMisMatch).ConfigureAwait(false);
                return;
            }

            ServerState = ServerState.Connected;

            // Reload all data
            await LoadIninitialPairsAsync().ConfigureAwait(false);
            await LoadOnlinePairsAsync().ConfigureAwait(false);
            Mediator.Publish(new ConnectedMessage(_connectionDto));
        }
        catch (Exception ex)
        {
            Logger.LogCritical(ex, "Failure to obtain data after reconnection");
            await StopConnectionAsync(ServerState.Disconnected).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Handles hub reconnecting events by updating state and logging
    /// </summary>
    /// <param name="arg">Exception that caused the reconnection, if any</param>
    private void MoonlightHubOnReconnecting(Exception? arg)
    {
        _doNotNotifyOnNextInfo = true;
        _healthCheckTokenSource?.Cancel();
        ServerState = ServerState.Reconnecting;
        Logger.LogWarning(arg, "Connection closed... Reconnecting");
        Mediator.Publish(new EventMessage(new Services.Events.Event(nameof(ApiController), Services.Events.EventSeverity.Warning,
            $"Connection interrupted, reconnecting to {_serverManager.CurrentServer.ServerName}")));
    }

    /// <summary>
    /// Refreshes authentication tokens and handles reconnection if token changes
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if reconnection is required, false otherwise</returns>
    private async Task<bool> RefreshTokenAsync(CancellationToken ct)
    {
        bool requireReconnect = false;
        try
        {
            var token = await _tokenProvider.GetOrUpdateToken(ct).ConfigureAwait(false);

            // Reconnect if token has changed
            if (!string.Equals(token, _lastUsedToken, StringComparison.Ordinal))
            {
                Logger.LogDebug("Reconnecting due to updated token");

                _doNotNotifyOnNextInfo = true;
                await CreateConnectionsAsync().ConfigureAwait(false);
                requireReconnect = true;
            }
        }
        catch (MoonlightAuthFailureException ex)
        {
            AuthFailureMessage = ex.Reason;
            await StopConnectionAsync(ServerState.Unauthorized).ConfigureAwait(false);
            requireReconnect = true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not refresh token, forcing reconnect");
            _doNotNotifyOnNextInfo = true;
            await CreateConnectionsAsync().ConfigureAwait(false);
            requireReconnect = true;
        }

        return requireReconnect;
    }

    /// <summary>
    /// Stops the current connection and cleans up resources
    /// </summary>
    /// <param name="state">The state to set after stopping the connection</param>
    private async Task StopConnectionAsync(ServerState state)
    {
        ServerState = ServerState.Disconnecting;

        Logger.LogInformation("Stopping existing connection");
        await _hubFactory.DisposeHubAsync().ConfigureAwait(false);

        if (_moonlightHub is not null)
        {
            Mediator.Publish(new EventMessage(new Services.Events.Event(nameof(ApiController), Services.Events.EventSeverity.Informational,
                $"Stopping existing connection to {_serverManager.CurrentServer.ServerName}")));

            // Clean up connection state
            _initialized = false;
            _healthCheckTokenSource?.Cancel();
            Mediator.Publish(new DisconnectedMessage());
            _moonlightHub = null;
            _connectionDto = null;
        }

        ServerState = state;
    }
}
#pragma warning restore MA0040