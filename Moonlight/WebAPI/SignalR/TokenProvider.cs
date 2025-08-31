using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Reflection;

using Microsoft.Extensions.Logging;

using Moonlight.Utils;
using MoonLight.API.Routes;
using Moonlight.Services;
using Moonlight.Services.Mediator;
using Moonlight.Services.ServerConfiguration;
using Moonlight.MoonlightConfiguration.Models;
using Moonlight.MNet;

namespace Moonlight.WebAPI.SignalR;

/// <summary>
/// Provides JWT token management for SignalR connections, handling token generation, renewal, and caching.
/// Supports both OAuth2 and secret key authentication methods.
/// </summary>
public sealed class TokenProvider : IDisposable, IMediatorSubscriber
{
    /// <summary>
    /// Service for interacting with Dalamud utilities and player information
    /// </summary>
    private readonly DalamudUtilService _dalamudUtil;

    /// <summary>
    /// HTTP client for making authentication requests to the server
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Logger instance for this token provider
    /// </summary>
    private readonly ILogger<TokenProvider> _logger;

    /// <summary>
    /// Manager for server configuration and connection settings
    /// </summary>
    private readonly ServerConfigurationManager _serverManager;

    /// <summary>
    /// Thread-safe cache storing JWT tokens by their identifier
    /// </summary>
    private readonly ConcurrentDictionary<JwtIdentifier, string> _tokenCache = new();

    /// <summary>
    /// Service for managing MNet configuration including API keys
    /// </summary>
    private readonly MNetConfigService _mnetConfigService;

    /// <summary>
    /// Initializes a new instance of the TokenProvider class with required dependencies.
    /// Sets up mediator subscriptions to clear token cache on login/logout events.
    /// </summary>
    /// <param name="logger">Logger for this token provider</param>
    /// <param name="serverManager">Server configuration manager</param>
    /// <param name="dalamudUtil">Dalamud utility service</param>
    /// <param name="moonlightMediator">Mediator for handling application events</param>
    /// <param name="httpClient">HTTP client for server requests</param>
    /// <param name="mnetConfigService">MNet configuration service</param>
    public TokenProvider(ILogger<TokenProvider> logger, ServerConfigurationManager serverManager, DalamudUtilService dalamudUtil, MoonlightMediator moonlightMediator, HttpClient httpClient, MNetConfigService mnetConfigService)
    {
        _logger = logger;
        _serverManager = serverManager;
        _dalamudUtil = dalamudUtil;

        // Get assembly version for potential future use
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        Mediator = moonlightMediator;
        _httpClient = httpClient;
        _mnetConfigService = mnetConfigService;

        // Subscribe to logout events to clear cached tokens and identifiers
        Mediator.Subscribe<DalamudLogoutMessage>(this, (_) =>
        {
            _lastJwtIdentifier = null;
            _tokenCache.Clear();
        });

        // Subscribe to login events to clear cached tokens and identifiers
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) =>
        {
            _lastJwtIdentifier = null;
            _tokenCache.Clear();
        });
    }

    /// <summary>
    /// Gets the mediator instance for publishing and subscribing to application events
    /// </summary>
    public MoonlightMediator Mediator { get; }

    /// <summary>
    /// Stores the last successfully created JWT identifier for fallback purposes
    /// </summary>
    private JwtIdentifier? _lastJwtIdentifier;

    /// <summary>
    /// Disposes of the token provider by unsubscribing from all mediator events
    /// </summary>
    public void Dispose()
    {
        Mediator.UnsubscribeAll(this);
    }

    /// <summary>
    /// Requests a new JWT token from the server, either as a fresh token or renewal.
    /// Handles both OAuth2 and secret key authentication methods.
    /// </summary>
    /// <param name="isRenewal">True if this is a token renewal, false for a new token</param>
    /// <param name="identifier">The JWT identifier containing authentication information</param>
    /// <param name="ct">Cancellation token for the operation</param>
    /// <returns>The JWT token string</returns>
    /// <exception cref="InvalidOperationException">Thrown when no secret key is available or token renewal fails</exception>
    /// <exception cref="MoonlightAuthFailureException">Thrown when authentication fails</exception>
    public async Task<string> GetNewToken(bool isRenewal, JwtIdentifier identifier, CancellationToken ct)
    {
        Uri tokenUri;
        string response = string.Empty;
        HttpResponseMessage result;

        try
        {
            if (!isRenewal)
            {
                _logger.LogDebug("GetNewToken: Requesting");

                // Handle secret key authentication (non-OAuth2)
                if (!_serverManager.CurrentServer.UseOAuth2)
                {
                    // Build the authentication endpoint URI
                    tokenUri = MoonLightAuth.AuthFullPath(new Uri(_serverManager.CurrentApiUrl
                        .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
                        .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));

                    // Prefer global mNet key when present, fallback to server-specific key
                    var secretKey = _mnetConfigService.Current.ApiKey;
                    if (string.IsNullOrEmpty(secretKey)) secretKey = _serverManager.GetSecretKey(out _);
                    if (string.IsNullOrEmpty(secretKey)) throw new InvalidOperationException("No secret key available (mNet/global or server-specific)");

                    // Hash the secret key for authentication
                    var auth = secretKey.GetHash256();
                    _logger.LogInformation("Sending SecretKey Request to server with auth {auth}", string.Join("", identifier.SecretKeyOrOAuth.Take(10)));

                    // Send POST request with form data containing auth hash and character identifier
                    result = await _httpClient.PostAsync(tokenUri, new FormUrlEncodedContent(
                    [
                            new KeyValuePair<string, string>("auth", auth),
                            new KeyValuePair<string, string>("charaIdent", await _dalamudUtil.GetPlayerNameHashedAsync().ConfigureAwait(false)),
                    ]), ct).ConfigureAwait(false);
                }
                else
                {
                    // Handle OAuth2 authentication
                    tokenUri = MoonLightAuth.AuthWithOauthFullPath(new Uri(_serverManager.CurrentApiUrl
                        .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
                        .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));

                    // Create POST request with OAuth2 credentials
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, tokenUri.ToString());
                    request.Content = new FormUrlEncodedContent([
                        new KeyValuePair<string, string>("uid", identifier.UID),
                        new KeyValuePair<string, string>("charaIdent", identifier.CharaHash)
                        ]);
                    // Set Bearer token authorization header
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identifier.SecretKeyOrOAuth);
                    _logger.LogInformation("Sending OAuth Request to server with auth {auth}", string.Join("", identifier.SecretKeyOrOAuth.Take(10)));
                    result = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
                }
            }
            else
            {
                // Handle token renewal using existing cached token
                _logger.LogDebug("GetNewToken: Renewal");

                // Build the token renewal endpoint URI
                tokenUri = MoonLightAuth.RenewTokenFullPath(new Uri(_serverManager.CurrentApiUrl
                    .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
                    .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));

                // Create GET request for token renewal
                HttpRequestMessage request = new(HttpMethod.Get, tokenUri.ToString());

                // Ensure we have a cached token to renew
                if (!_tokenCache.TryGetValue(identifier, out var currentToken))
                    throw new InvalidOperationException("No cached token to renew");

                // Set Bearer token authorization header with current token
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", currentToken);
                result = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            }

            // Read the response content (should be the JWT token)
            response = await result.Content.ReadAsStringAsync().ConfigureAwait(false);
            // Ensure the HTTP request was successful
            result.EnsureSuccessStatusCode();
            // Cache the new token
            _tokenCache[identifier] = response;
        }
        catch (HttpRequestException ex)
        {
            // Remove failed token from cache
            _tokenCache.TryRemove(identifier, out _);

            _logger.LogError(ex, "GetNewToken: Failure to get token");

            // Handle unauthorized responses specifically
            if (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // Show appropriate error message based on whether this was a renewal or new token request
                if (isRenewal)
                    Mediator.Publish(new NotificationMessage("Error refreshing token", "Your authentication token could not be renewed. Try reconnecting to Moonlight manually.",
                    NotificationType.Error));
                else
                    Mediator.Publish(new NotificationMessage("Error generating token", "Your authentication token could not be generated. Check Moonlights Main UI (/Moonlight in chat) to see the error message.",
                    NotificationType.Error));

                // Publish disconnection event
                Mediator.Publish(new DisconnectedMessage());
                throw new MoonlightAuthFailureException(response);
            }

            throw;
        }

        // Parse and validate the JWT token
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(response);

        // Log token prefix for debugging (avoid logging full token for security)
        _logger.LogTrace("GetNewToken: JWT prefix {prefix}...", string.Join("", response.Take(10)));
        _logger.LogDebug("GetNewToken: Valid until {date}, ValidClaim until {date}", jwtToken.ValidTo,
                new DateTime(long.Parse(jwtToken.Claims.Single(c => string.Equals(c.Type, "expiration_date", StringComparison.Ordinal)).Value), DateTimeKind.Utc));

        // Validate token time against system clock (within 10 minute tolerance)
        var dateTimeMinus10 = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10));
        var dateTimePlus10 = DateTime.UtcNow.Add(TimeSpan.FromMinutes(10));
        var tokenTime = jwtToken.ValidTo;

        // Check if token time is reasonable compared to system time
        if (tokenTime <= dateTimeMinus10 || tokenTime >= dateTimePlus10)
        {
            // Remove invalid token from cache
            _tokenCache.TryRemove(identifier, out _);
            // Notify user of system clock issue
            Mediator.Publish(new NotificationMessage("Invalid system clock", "The clock of your computer is invalid. " +
                "Moonlight will not function properly if the time zone is not set correctly. " +
                "Please set your computers time zone correctly and keep your clock synchronized with the internet.",
                NotificationType.Error));
            throw new InvalidOperationException($"JwtToken is behind DateTime.UtcNow, DateTime.UtcNow is possibly wrong. DateTime.UtcNow is {DateTime.UtcNow}, JwtToken.ValidTo is {jwtToken.ValidTo}");
        }
        return response;
    }

    /// <summary>
    /// Creates a JWT identifier based on current server configuration and player information.
    /// Handles both OAuth2 and secret key authentication methods.
    /// </summary>
    /// <returns>A JWT identifier or null if player information is unavailable</returns>
    private async Task<JwtIdentifier?> GetIdentifier()
    {
        JwtIdentifier jwtIdentifier;
        try
        {
            // Get the hashed player identifier from Dalamud
            var playerIdentifier = await _dalamudUtil.GetPlayerNameHashedAsync().ConfigureAwait(false);

            // If no player identifier available, return the last known identifier
            if (string.IsNullOrEmpty(playerIdentifier))
            {
                _logger.LogTrace("GetIdentifier: PlayerIdentifier was null, returning last identifier {identifier}", _lastJwtIdentifier);
                return _lastJwtIdentifier;
            }

            // Create identifier based on authentication method
            if (_serverManager.CurrentServer.UseOAuth2)
            {
                // Get OAuth2 credentials from server manager
                var (OAuthToken, UID) = _serverManager.GetOAuth2(out _)
                    ?? throw new InvalidOperationException("Requested OAuth2 but received null");

                // Create OAuth2-based identifier
                jwtIdentifier = new(_serverManager.CurrentApiUrl,
                    playerIdentifier,
                    UID, OAuthToken);
            }
            else
            {
                // Get secret key, preferring global mNet key over server-specific key
                var secretKey = _mnetConfigService.Current.ApiKey;
                if (string.IsNullOrEmpty(secretKey))
                    secretKey = _serverManager.GetSecretKey(out _)
                        ?? throw new InvalidOperationException("Requested SecretKey but received null");

                // Create secret key-based identifier
                jwtIdentifier = new(_serverManager.CurrentApiUrl,
                                    playerIdentifier,
                                    string.Empty,
                                    secretKey);
            }
            // Cache the identifier for future fallback use
            _lastJwtIdentifier = jwtIdentifier;
        }
        catch (Exception ex)
        {
            // If we can't create a new identifier, try to use the last known one
            if (_lastJwtIdentifier == null)
            {
                _logger.LogError("GetIdentifier: No last identifier found, aborting");
                return null;
            }

            _logger.LogWarning(ex, "GetIdentifier: Could not get JwtIdentifier for some reason or another, reusing last identifier {identifier}", _lastJwtIdentifier);
            jwtIdentifier = _lastJwtIdentifier;
        }

        _logger.LogDebug("GetIdentifier: Using identifier {identifier}", jwtIdentifier);
        return jwtIdentifier;
    }

    /// <summary>
    /// Gets a cached JWT token if available, without attempting to refresh or create a new one.
    /// </summary>
    /// <returns>The cached JWT token or null if no token is available</returns>
    /// <exception cref="InvalidOperationException">Thrown when no token is present in cache</exception>
    public async Task<string?> GetToken()
    {
        // Get the current JWT identifier
        JwtIdentifier? jwtIdentifier = await GetIdentifier().ConfigureAwait(false);
        if (jwtIdentifier == null) return null;

        // Return cached token if available
        if (_tokenCache.TryGetValue(jwtIdentifier, out var token))
        {
            return token;
        }

        throw new InvalidOperationException("No token present");
    }

    /// <summary>
    /// Gets a JWT token from cache or creates/renews one if needed.
    /// Automatically handles token expiration and renewal.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation</param>
    /// <returns>A valid JWT token or null if identifier cannot be created</returns>
    public async Task<string?> GetOrUpdateToken(CancellationToken ct)
    {
        // Get the current JWT identifier
        JwtIdentifier? jwtIdentifier = await GetIdentifier().ConfigureAwait(false);
        if (jwtIdentifier == null) return null;

        bool renewal = false;

        // Check if we have a cached token
        if (_tokenCache.TryGetValue(jwtIdentifier, out var token))
        {
            // Parse the cached token to check expiration
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);

            // If token is still valid (more than 5 minutes remaining), return it
            if (jwt.ValidTo != DateTime.MinValue && jwt.ValidTo.Subtract(TimeSpan.FromMinutes(5)) > DateTime.UtcNow)
            {
                return token;
            }

            _logger.LogDebug("GetOrUpdate: Cached token requires renewal, token valid to: {valid}, UtcTime is {utcTime}", jwt.ValidTo, DateTime.UtcNow);
            renewal = true;
        }
        else
        {
            _logger.LogDebug("GetOrUpdate: Did not find token in cache, requesting a new one");
        }

        _logger.LogTrace("GetOrUpdate: Getting new token");
        // Get a new token (either fresh or renewal)
        return await GetNewToken(renewal, jwtIdentifier, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Attempts to update the OAuth2 login token if it's nearing expiration or if forced.
    /// Only works when the server is configured for OAuth2 authentication.
    /// </summary>
    /// <param name="currentServer">The server storage configuration to update</param>
    /// <param name="forced">Whether to force token renewal regardless of expiration</param>
    /// <returns>True if the token was successfully updated or is still valid, false otherwise</returns>
    public async Task<bool> TryUpdateOAuth2LoginTokenAsync(ServerStorage currentServer, bool forced = false)
    {
        // Get OAuth2 credentials from server manager
        var oauth2 = _serverManager.GetOAuth2(out _);
        if (oauth2 == null) return false;

        // Parse the current OAuth2 token
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(oauth2.Value.OAuthToken);

        // Check if renewal is needed (unless forced)
        if (!forced)
        {
            // If token is valid for more than 7 days, no renewal needed
            if (jwt.ValidTo == DateTime.MinValue || jwt.ValidTo.Subtract(TimeSpan.FromDays(7)) > DateTime.Now)
                return true;

            // If token is already expired, renewal will fail
            if (jwt.ValidTo < DateTime.UtcNow)
                return false;
        }

        // Build the OAuth2 token renewal endpoint URI
        var tokenUri = MoonLightAuth.RenewOAuthTokenFullPath(new Uri(currentServer.ServerUri
            .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
            .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));

        // Create POST request for token renewal
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, tokenUri.ToString());
        // Set Bearer token authorization header with current OAuth2 token
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", oauth2.Value.OAuthToken);
        _logger.LogInformation("Sending Request to server with auth {auth}", string.Join("", oauth2.Value.OAuthToken.Take(10)));

        // Send the renewal request
        var result = await _httpClient.SendAsync(request).ConfigureAwait(false);

        // Handle unsuccessful renewal
        if (!result.IsSuccessStatusCode)
        {
            _logger.LogWarning("Could not renew OAuth2 Login token, error code {error}", result.StatusCode);
            // Clear the invalid OAuth2 token and save configuration
            currentServer.OAuthToken = null;
            _serverManager.Save();
            return false;
        }

        // Read the new token and update server configuration
        var newToken = await result.Content.ReadAsStringAsync().ConfigureAwait(false);
        currentServer.OAuthToken = newToken;
        _serverManager.Save();

        return true;
    }
}