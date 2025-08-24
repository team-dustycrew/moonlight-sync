using Microsoft.Extensions.Logging;
using Moonlight.MoonlightConfiguration;

namespace Moonlight.MNet;

public class MNetDevicePairingService
{
    private readonly ILogger<MNetDevicePairingService> _logger;
    private readonly MNetClient _client;
    private readonly MNetConfigService _configService;

    public MNetDevicePairingService(ILogger<MNetDevicePairingService> logger, MNetClient client, MNetConfigService configService)
    {
        _logger = logger;
        _client = client;
        _configService = configService;
    }

    public async Task<(string userCode, string verificationUri, string deviceCode, DateTime expiresAt, int intervalSeconds)> StartAsync(CancellationToken ct)
    {
        _client.BaseUrl = _configService.Current.BaseUrl;
        var resp = await _client.StartDevicePairingAsync(ct).ConfigureAwait(false);
        var expiresAt = DateTime.UtcNow.AddSeconds(resp.expires_in);
        return (resp.user_code, resp.verification_uri, resp.device_code, expiresAt, resp.interval);
    }

    public async Task<string?> PollForKeyAsync(string deviceCode, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var poll = await _client.PollDevicePairingAsync(deviceCode, ct).ConfigureAwait(false);
            if (string.Equals(poll.status, "approved", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(poll.key))
            {
                return poll.key;
            }
            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }
        return null;
    }

    public async Task<bool> SaveKeyAndConfirmAsync(string apiKey, CancellationToken ct)
    {
        var identity = await _client.ResolveIdentityAsync(apiKey, ct).ConfigureAwait(false);
        _configService.Current.ApiKey = apiKey;
        _configService.Current.LastResolvedIdentity = identity?.discord?.username ?? string.Empty;
        _configService.Current.UpdatedAtUtc = DateTime.UtcNow;
        _configService.Save();
        return true;
    }
}


