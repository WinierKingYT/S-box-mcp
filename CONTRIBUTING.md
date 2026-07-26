# Contributing

Thank you for improving s&box MCP AI Bridge.

## Development requirements

- Windows
- .NET 10 SDK
- s&box installed through Steam for editor builds and tests

The bridge and repository checks do not require s&box:

```powershell
.\scripts\validate-repository.ps1
dotnet test .\tools\SboxMcp.StdioBridge.Tests\SboxMcp.StdioBridge.Tests.csproj
```

Run the complete validation before opening a pull request:

```powershell
dotnet test .\mcp_server.slnx -p:StaticWebAssetsEnabled=false
```

For a non-default s&box location:

```powershell
dotnet test .\mcp_server.slnx `
  -p:StaticWebAssetsEnabled=false `
  -p:SboxRoot="D:\SteamLibrary\steamapps\common\sbox"
```

## Pull requests

- Keep changes focused and explain user-visible behavior.
- Add or update tests for protocol, security, configuration, or scene mutations.
- Never commit API keys, local MCP configuration, `.sbox`, `dist`, `bin`, or `obj`.
- Preserve the loopback-only and API-key security defaults.
- Update both `README.md` and `README.en.md` for user-facing changes.

No public license has been selected yet. Please open an issue before preparing
a substantial external contribution.
