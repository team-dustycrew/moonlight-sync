using MessagePack;
using MessagePack.Resolvers;

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using MoonLight.API.SignalR;
using Moonlight.Services;
using Moonlight.Services.Mediator;
using Moonlight.Services.ServerConfiguration;
using Moonlight.WebAPI.SignalR.Utils;
using System.Text;

namespace Moonlight.WebAPI.SignalR;

/// <summary>
/// Factory class responsible for creating and managing SignalR HubConnection instances.
/// Handles connection lifecycle, transport configuration, and Wine compatibility.
/// </summary>
public class HubFactory : MediatorSubscriberBase
{
    /// <summary>
    /// Logger provider for the SignalR hub connection
    /// </summary>
    private readonly ILoggerProvider _loggingProvider;

    /// <summary>
    /// Manager for server configuration settings and endpoints
    /// </summary>
    private readonly ServerConfigurationManager _serverConfigurationManager;

    /// <summary>
    /// Provider for authentication tokens used in SignalR connections
    /// </summary>
    private readonly TokenProvider _tokenProvider;

    /// <summary>
    /// Current SignalR hub connection instance, null if not created or disposed
    /// </summary>
    private HubConnection? _instance;

    /// <summary>
    /// Flag indicating whether the factory has been disposed
    /// </summary>
    private bool _isDisposed = false;

    /// <summary>
    /// Flag indicating whether the application is running under Wine (affects transport selection)
    /// </summary>
    private readonly bool _isWine = false;

    /// <summary>
    /// Initializes a new instance of the HubFactory class.
    /// </summary>
    /// <param name="logger">Logger for this factory instance</param>
    /// <param name="mediator">Mediator for publishing connection events</param>
    /// <param name="serverConfigurationManager">Manager for server configuration settings</param>
    /// <param name="tokenProvider">Provider for authentication tokens</param>
    /// <param name="pluginLog">Logger provider for the SignalR hub</param>
    /// <param name="dalamudUtilService">Service to detect Wine environment</param>
    public HubFactory(ILogger<HubFactory> logger, MoonlightMediator mediator, ServerConfigurationManager serverConfigurationManager, TokenProvider tokenProvider, ILoggerProvider pluginLog, DalamudUtilService dalamudUtilService) : base(logger, mediator)
    {
        _serverConfigurationManager = serverConfigurationManager;
        _tokenProvider = tokenProvider;
        _loggingProvider = pluginLog;
        _isWine = dalamudUtilService.IsWine;
    }

    /// <summary>
    /// Disposes the current HubConnection instance if it exists.
    /// Unsubscribes from events, stops the connection, and cleans up resources.
    /// </summary>
    public async Task DisposeHubAsync()
    {
        if (_instance == null || _isDisposed) return;

        Logger.LogDebug("Disposing current HubConnection");

        _isDisposed = true;

        // Unsubscribe from connection events to prevent callbacks during disposal
        _instance.Closed -= HubOnClosed;
        _instance.Reconnecting -= HubOnReconnecting;
        _instance.Reconnected -= HubOnReconnected;

        // Gracefully stop and dispose the connection
        await _instance.StopAsync().ConfigureAwait(false);
        await _instance.DisposeAsync().ConfigureAwait(false);

        _instance = null;

        Logger.LogDebug("Current HubConnection disposed");
    }

    /// <summary>
    /// Gets the existing HubConnection instance or creates a new one if needed.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation</param>
    /// <returns>A configured HubConnection instance</returns>
    public HubConnection GetOrCreate(CancellationToken ct)
    {
        // Return existing instance if it's still valid
        if (_isDisposed == false && _instance != null) return _instance;

        // Build a new HubConnection
        return BuildHubConnection(ct);
    }

    /// <summary>
    /// Builds a new HubConnection with appropriate transport configuration and message pack protocol.
    /// Handles Wine compatibility by falling back from WebSockets when necessary.
    /// </summary>
    /// <param name="ct">Cancellation token for token provider operations</param>
    /// <returns>A fully configured HubConnection instance</returns>
    private HubConnection BuildHubConnection(CancellationToken ct)
    {
        // Determine transport types based on server configuration
        // Fall back through transport types in order of preference
        // MessagePack uses Binary transfer format; ServerSentEvents does not support Binary.
        // Always exclude ServerSentEvents from the allowed transports to avoid runtime failures
        // like: "The transport does not support the 'Binary' transfer format."
        var transportType = _serverConfigurationManager.GetTransport() switch
        {
            HttpTransportType.None => HttpTransportType.WebSockets | HttpTransportType.LongPolling,
            HttpTransportType.WebSockets => HttpTransportType.WebSockets | HttpTransportType.LongPolling,
            HttpTransportType.ServerSentEvents => HttpTransportType.LongPolling, // force LP instead of SSE
            HttpTransportType.LongPolling => HttpTransportType.LongPolling,
            _ => HttpTransportType.WebSockets | HttpTransportType.LongPolling
        };

        // Wine has compatibility issues with WebSockets, so fall back to other transports
        if (_isWine && !_serverConfigurationManager.CurrentServer.ForceWebSockets && transportType.HasFlag(HttpTransportType.WebSockets))
        {
            Logger.LogDebug("Wine detected, forcing LongPolling (SSE incompatible with MessagePack Binary)");
            transportType = HttpTransportType.LongPolling;
        }

        Logger.LogDebug("Building new HubConnection using transport {transport}", transportType);

        // Build the HubConnection with all necessary configuration
        _instance = new HubConnectionBuilder().WithUrl(_serverConfigurationManager.CurrentApiUrl + IMoonLightHub.Path, options =>
        {
            // Configure authentication token provider
            options.AccessTokenProvider = () => _tokenProvider.GetOrUpdateToken(ct);
            options.Transports = transportType;

            // Always add API key header for negotiation/authentication
            var apiKey = _serverConfigurationManager.GetMNetKey();
            if (string.IsNullOrEmpty(apiKey) == false)
            {
                // Sanitize API key to prevent header injection
                apiKey = apiKey.Replace("\r", string.Empty).Replace("\n", string.Empty);
                options.Headers.Add("X-MNet-Key", apiKey);
                // Ensure header presence and log each outgoing negotiate/connect request
                options.HttpMessageHandlerFactory = (inner) => new HeaderInjectingHandler(inner, apiKey, Logger);
                // Ensure header is also present on WebSocket upgrade
                options.WebSocketConfiguration = ws =>
                {
                    try { ws.SetRequestHeader("X-MNet-Key", apiKey); }
                    catch { /* ignore */ }
                };
            }
            else
            {
                Logger.LogWarning("No mNet API key configured; SignalR negotiation will likely be unauthorized");
            }
        })
        // Configure automatic reconnection with custom retry policy
        .WithAutomaticReconnect(new ForeverRetryPolicy(Mediator))
        // Setup logging configuration
        .ConfigureLogging(a =>
        {
            a.ClearProviders().AddProvider(_loggingProvider);
            a.SetMinimumLevel(LogLevel.Information);
        })
        .Build();

        // Subscribe to connection events for mediator notifications
        _instance.Closed += HubOnClosed;
        _instance.Reconnecting += HubOnReconnecting;
        _instance.Reconnected += HubOnReconnected;

        _isDisposed = false;

        return _instance;
    }

    /// <summary>
    /// Custom HTTP message handler that injects authentication headers and ensures proper content for SignalR requests.
    /// This handler intercepts HTTP requests made by the SignalR client and modifies them as needed.
    /// </summary>
    private sealed class HeaderInjectingHandler : DelegatingHandler
    {
        /// <summary>
        /// The API key to inject into HTTP requests for authentication with the mNet service
        /// </summary>
        private readonly string _apiKey;

        /// <summary>
        /// Logger instance for tracing HTTP request modifications and debugging
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the HeaderInjectingHandler class.
        /// </summary>
        /// <param name="innerHandler">The inner HTTP message handler to delegate to</param>
        /// <param name="apiKey">The API key to inject into requests</param>
        /// <param name="logger">Logger for tracing request modifications</param>
        public HeaderInjectingHandler(HttpMessageHandler innerHandler, string apiKey, ILogger logger) : base(innerHandler)
        {
            _apiKey = apiKey;
            _logger = logger;
        }

        /// <summary>
        /// Intercepts and modifies HTTP requests before they are sent.
        /// Injects the X-MNet-Key header for authentication and ensures negotiate requests have proper JSON content.
        /// </summary>
        /// <param name="request">The HTTP request message to modify</param>
        /// <param name="cancellationToken">Cancellation token for the request</param>
        /// <returns>Task representing the HTTP response</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Inject the API key header if it's not already present and we have a valid key
            if (request.Headers.Contains("X-MNet-Key") == false && string.IsNullOrEmpty(_apiKey) == false)
            {
                request.Headers.TryAddWithoutValidation("X-MNet-Key", _apiKey);
            }

            // Some servers expect a JSON body for negotiate requests, even if empty
            // This ensures compatibility with SignalR negotiate endpoints that require content
            if (request.Method == HttpMethod.Post && request.RequestUri != null && request.RequestUri.AbsolutePath.Contains("/negotiate", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Content == null)
                {
                    // Set empty JSON object as content with proper content type
                    request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                }
            }

            // Log without exposing header values
            _logger.LogTrace("SignalR HTTP {method} {uri} (auth header present: {present})", request.Method, request.RequestUri, request.Headers.Contains("X-MNet-Key"));

            // Continue with the request using the base handler
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Handles the hub connection closed event and publishes it through the mediator.
    /// </summary>
    /// <param name="arg">Exception that caused the connection to close, if any</param>
    private Task HubOnClosed(Exception? arg)
    {
        Mediator.Publish(new HubClosedMessage(arg));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles the hub connection reconnected event and publishes it through the mediator.
    /// </summary>
    /// <param name="arg">Connection ID of the reconnected connection</param>
    private Task HubOnReconnected(string? arg)
    {
        Mediator.Publish(new HubReconnectedMessage(arg));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles the hub connection reconnecting event and publishes it through the mediator.
    /// </summary>
    /// <param name="arg">Exception that caused the reconnection attempt, if any</param>
    private Task HubOnReconnecting(Exception? arg)
    {
        Mediator.Publish(new HubReconnectingMessage(arg));
        return Task.CompletedTask;
    }
}