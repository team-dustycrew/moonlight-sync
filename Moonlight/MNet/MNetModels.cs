using System;

namespace Moonlight.MNet;

public record MNetDeviceStartResponse
(
    string device_code,
    string user_code,
    string verification_uri,
    int expires_in,
    int interval
);

public record MNetDevicePollResponse
(
    string status,
    string? key
);

public record MNetIdentity
(
    MNetDiscord discord,
    MNetLodestone lodestone
);

public record MNetDiscord
(
    string id,
    string username,
    string? avatar
);

public record MNetLodestone
(
    string characterId,
    string name,
    string world
);


