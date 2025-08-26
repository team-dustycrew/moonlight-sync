using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace Moonlight.MNet;

public class MNetClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MNetClient> _logger;

    public string BaseUrl { get; set; } = "https://mnet.live";

    public MNetClient(ILogger<MNetClient> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<MNetDeviceStartResponse> StartDevicePairingAsync(CancellationToken ct)
    {
        var url = MNetRoutes.BuildUri(BaseUrl, MNetRoutes.DeviceStart);
        using var response = await _httpClient.PostAsync(url, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<MNetDeviceStartResponse>(raw)!;
    }

    public async Task<MNetDevicePollResponse> PollDevicePairingAsync(string deviceCode, CancellationToken ct)
    {
        var url = MNetRoutes.BuildUri(BaseUrl, MNetRoutes.DevicePoll);
        var payload = JsonContent.Create(new { device_code = deviceCode });
        using var response = await _httpClient.PostAsync(url, payload, ct).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<MNetDevicePollResponse>(raw)!;
    }

    public async Task<MNetIdentity?> ResolveIdentityAsync(string apiKey, CancellationToken ct)
    {
        // Try configured BaseUrl first
        var identity = await ResolveIdentityAgainstAsync(BaseUrl, apiKey, ct).ConfigureAwait(false);
        if (identity != null)
            return identity;

        // Fallback: try without www if present
        var altBase = BaseUrl.Replace("://www.", "://");
        if (!string.Equals(altBase, BaseUrl, StringComparison.Ordinal))
        {
            _logger.LogInformation("mNet resolve retrying without www: {base}", altBase);
            identity = await ResolveIdentityAgainstAsync(altBase, apiKey, ct).ConfigureAwait(false);
            if (identity != null)
                return identity;
        }

        return null;
    }

    private async Task<MNetIdentity?> ResolveIdentityAgainstAsync(string baseUrl, string apiKey, CancellationToken ct)
    {
        try
        {
            var url = MNetRoutes.BuildUri(baseUrl, MNetRoutes.ResolveIdentity);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("X-MNet-Key", apiKey);
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("mNet resolve failed: {status} {body}", response.StatusCode, raw);
                return null;
            }
            return JsonSerializer.Deserialize<MNetIdentity>(raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "mNet resolve exception against {base}", baseUrl);
            return null;
        }
    }
}


