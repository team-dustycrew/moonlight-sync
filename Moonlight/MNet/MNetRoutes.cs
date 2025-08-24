using System;

namespace Moonlight.MNet;

public static class MNetRoutes
{
    public const string DeviceStart = "/v1/device/start";
    public const string DevicePoll = "/v1/device/poll";
    public const string ResolveIdentity = "/v1/resolve";
    public const string Attest = "/v1/attest";

    public static Uri BuildUri(string baseUrl, string path)
    {
        baseUrl = baseUrl.TrimEnd('/');
        path = path.StartsWith('/') ? path : "/" + path;
        return new Uri(baseUrl + path);
    }
}


