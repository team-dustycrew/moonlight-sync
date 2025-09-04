using Microsoft.Extensions.Logging;
using Moonlight.MoonlightConfiguration;
using Moonlight.Services.Mediator;
using Moonlight.WebAPI.Files.Models;
using Moonlight.WebAPI.SignalR;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;

namespace Moonlight.WebAPI.Files;

public class FileTransferOrchestrator : DisposableMediatorSubscriberBase
{
    private readonly ConcurrentDictionary<Guid, bool> _downloadReady = new();
    private readonly HttpClient _httpClient;
    private readonly MoonlightConfigService _moonlightConfig;
    private readonly Moonlight.Services.ServerConfiguration.ServerConfigurationManager _serverConfigurationManager;
    private readonly TokenProvider _tokenProvider;
    private readonly object _semaphoreModificationLock = new();
    private int _availableDownloadSlots;
    private SemaphoreSlim _downloadSemaphore;
    private int CurrentlyUsedDownloadSlots => _availableDownloadSlots - _downloadSemaphore.CurrentCount;

    public FileTransferOrchestrator(ILogger<FileTransferOrchestrator> logger, MoonlightConfigService moonlightConfig,
        MoonlightMediator mediator, Moonlight.Services.ServerConfiguration.ServerConfigurationManager serverConfigurationManager, HttpClient httpClient, TokenProvider tokenProvider) : base(logger, mediator)
    {
        _moonlightConfig = moonlightConfig;
        _serverConfigurationManager = serverConfigurationManager;
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Moonlight", ver!.Major + "." + ver!.Minor + "." + ver!.Build));

        _availableDownloadSlots = moonlightConfig.Current.ParallelDownloads;
        _downloadSemaphore = new(_availableDownloadSlots, _availableDownloadSlots);

        Mediator.Subscribe<ConnectedMessage>(this, (msg) =>
        {
            FilesCdnUri = msg.Connection.ServerInfo.FileServerAddress;
        });

        Mediator.Subscribe<DisconnectedMessage>(this, (msg) =>
        {
            FilesCdnUri = null;
        });
        Mediator.Subscribe<DownloadReadyMessage>(this, (msg) =>
        {
            _downloadReady[msg.RequestId] = true;
        });
    }

    public Uri? FilesCdnUri { private set; get; }
    public List<FileTransfer> ForbiddenTransfers { get; } = [];
    public bool IsInitialized => FilesCdnUri != null;

    public void ClearDownloadRequest(Guid guid)
    {
        _downloadReady.Remove(guid, out _);
    }

    public bool IsDownloadReady(Guid guid)
    {
        if (_downloadReady.TryGetValue(guid, out bool isReady) && isReady)
        {
            return true;
        }

        return false;
    }

    public void ReleaseDownloadSlot()
    {
        try
        {
            _downloadSemaphore.Release();
            Mediator.Publish(new DownloadLimitChangedMessage());
        }
        catch (SemaphoreFullException)
        {
            // ignore
        }
    }

    public async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, Uri uri,
        CancellationToken? ct = null, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead)
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        return await SendRequestInternalAsync(requestMessage, ct, httpCompletionOption).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> SendRequestAsync<T>(HttpMethod method, Uri uri, T content, CancellationToken ct) where T : class
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        if (content is not ByteArrayContent)
            requestMessage.Content = JsonContent.Create(content);
        else
            requestMessage.Content = content as ByteArrayContent;
        return await SendRequestInternalAsync(requestMessage, ct).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> SendRequestStreamAsync(HttpMethod method, Uri uri, ProgressableStreamContent content, CancellationToken ct)
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        requestMessage.Content = content;
        return await SendRequestInternalAsync(requestMessage, ct).ConfigureAwait(false);
    }

    public async Task WaitForDownloadSlotAsync(CancellationToken token)
    {
        lock (_semaphoreModificationLock)
        {
            if (_availableDownloadSlots != _moonlightConfig.Current.ParallelDownloads && _availableDownloadSlots == _downloadSemaphore.CurrentCount)
            {
                _availableDownloadSlots = _moonlightConfig.Current.ParallelDownloads;
                _downloadSemaphore = new(_availableDownloadSlots, _availableDownloadSlots);
            }
        }

        await _downloadSemaphore.WaitAsync(token).ConfigureAwait(false);
        Mediator.Publish(new DownloadLimitChangedMessage());
    }

    public long DownloadLimitPerSlot()
    {
        var limit = _moonlightConfig.Current.DownloadSpeedLimitInBytes;
        if (limit <= 0) return 0;
        limit = _moonlightConfig.Current.DownloadSpeedType switch
        {
            MoonlightConfiguration.Models.DownloadSpeeds.Bps => limit,
            MoonlightConfiguration.Models.DownloadSpeeds.KBps => limit * 1024,
            MoonlightConfiguration.Models.DownloadSpeeds.MBps => limit * 1024 * 1024,
            _ => limit,
        };
        var currentUsedDlSlots = CurrentlyUsedDownloadSlots;
        var avaialble = _availableDownloadSlots;
        var currentCount = _downloadSemaphore.CurrentCount;
        var dividedLimit = limit / (currentUsedDlSlots == 0 ? 1 : currentUsedDlSlots);
        if (dividedLimit < 0)
        {
            Logger.LogWarning("Calculated Bandwidth Limit is negative, returning Infinity: {value}, CurrentlyUsedDownloadSlots is {currentSlots}, " +
                "DownloadSpeedLimit is {limit}, available slots: {avail}, current count: {count}", dividedLimit, currentUsedDlSlots, limit, avaialble, currentCount);
            return long.MaxValue;
        }
        return Math.Clamp(dividedLimit, 1, long.MaxValue);
    }

    private async Task<HttpResponseMessage> SendRequestInternalAsync(HttpRequestMessage requestMessage, CancellationToken? ct = null, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead)
    {
        // Use Authorization: Bearer <jwt> for REST APIs
        var _ct = ct ?? new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
        string? jwt = null;
        try
        {
            jwt = await _tokenProvider.GetOrUpdateToken(_ct).ConfigureAwait(false);
        }
        catch
        {
            // fall back to no auth; caller should handle 401 reactively
        }
        if (!string.IsNullOrEmpty(jwt))
        {
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        }
        // Remove legacy header if present
        if (requestMessage.Headers.Contains("X-MNet-Key")) requestMessage.Headers.Remove("X-MNet-Key");

        if (requestMessage.Content != null && requestMessage.Content is not StreamContent && requestMessage.Content is not ByteArrayContent)
        {
            var content = await ((JsonContent)requestMessage.Content).ReadAsStringAsync().ConfigureAwait(false);
            Logger.LogDebug("Sending {method} to {uri} (Content: {content})", requestMessage.Method, requestMessage.RequestUri, content);
        }
        else
        {
            Logger.LogDebug("Sending {method} to {uri}", requestMessage.Method, requestMessage.RequestUri);
        }

        try
        {
            var response = ct != null
                ? await _httpClient.SendAsync(requestMessage, httpCompletionOption, ct.Value).ConfigureAwait(false)
                : await _httpClient.SendAsync(requestMessage, httpCompletionOption).ConfigureAwait(false);

            // Reactive fallback: on 401, try once to renew token and retry
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Logger.LogWarning("401 received for {uri}, attempting token renewal and retry", requestMessage.RequestUri);
                try
                {
                    var renewed = await _tokenProvider.ForceRenewToken(ct ?? new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token).ConfigureAwait(false);
                    // rebuild request to avoid disposed content streams
                    using var retry = new HttpRequestMessage(requestMessage.Method, requestMessage.RequestUri);
                    retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", renewed);
                    if (requestMessage.Content != null)
                    {
                        // If original content is JSON, re-create from string; otherwise, forward as-is when possible
                        if (requestMessage.Content is JsonContent)
                        {
                            var json = await ((JsonContent)requestMessage.Content).ReadAsStringAsync().ConfigureAwait(false);
                            retry.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                        }
                        else if (requestMessage.Content is ByteArrayContent or StreamContent)
                        {
                            retry.Content = requestMessage.Content;
                        }
                    }
                    response.Dispose();
                    return ct != null
                        ? await _httpClient.SendAsync(retry, httpCompletionOption, ct.Value).ConfigureAwait(false)
                        : await _httpClient.SendAsync(retry, httpCompletionOption).ConfigureAwait(false);
                }
                catch (Exception exRenew)
                {
                    Logger.LogWarning(exRenew, "Token renewal failed for retry of {uri}", requestMessage.RequestUri);
                }
            }

            return response;
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error during SendRequestInternal for {uri}", requestMessage.RequestUri);
            throw;
        }
    }
}