# Moonlight Dalamud Plugin

## Build dependencies

- Dalamud runtime libraries are required to compile with `Dalamud.NET.Sdk`.
- In CI we download the latest dev distrib from the official mirror and set `DALAMUD_HOME` automatically.
- Locally, you can either install Dalamud via XIVLauncher or use the bundled `DalamudSource/` folder in this repo.

### Why we vend `DalamudSource/`

- We keep a copy of the dev runtime DLLs (currently aligned with Packager 12.0.0) to make local builds work without a separate install.
- The project resolves `$(DalamudLibPath)` to:
  1. `$(DALAMUD_HOME)` if set (preferred), or
  2. `DalamudSource/` as a fallback.

### CI behavior (GitHub Actions)

- The workflow fetches `https://goatcorp.github.io/dalamud-distrib/latest.zip` and sets `DALAMUD_HOME` to that extraction path.
- This means GitHub Actions always uses the latest Dalamud runtime for builds.

### Optional NuGet source (DalamudSource)

- If you host packages like `DalamudPackager` 12.0.0 in a private feed, add a `nuget.config` at repo root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="DalamudSource" value="https://YOUR_DALAMUD_SOURCE_URL_HERE" />
  </packageSources>
</configuration>
```

- Note: the project uses `Dalamud.NET.Sdk/13.0.0` from nuget.org. We do not reference `DalamudPackager` directly to avoid version conflicts.
