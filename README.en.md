# s&box MCP AI Bridge

A local Model Context Protocol server for inspecting and editing an s&box
Editor scene from Codex, Claude Desktop, Cursor, or Windsurf. The server binds
only to `localhost` and requires an `x-api-key` on every MCP request.

[Türkçe dokümantasyon](README.md)

## Features

- Streamable HTTP (`/mcp`) with legacy SSE compatibility
- Scene hierarchy, selection, object, and component-property queries
- Object creation, deletion, and transform/component updates
- Native s&box Ctrl+Z/Ctrl+Y history for AI changes
- `undo_scene_change` and `redo_scene_change`, restricted to MCP history entries
- Viewport screenshots, camera focus, and opt-in console commands
- API-key authentication, rate limiting, CORS controls, and safe output paths

## Quick installation

1. Extract the release ZIP.
2. Open PowerShell in the extracted directory.
3. Run:

   ```powershell
   Set-ExecutionPolicy -Scope Process Bypass
   .\install.ps1
   ```

4. Fully restart s&box and every configured AI client.
5. Open a scene in s&box Editor.
6. Check `http://localhost:29017/health`.

For a non-default Steam library:

```powershell
.\install.ps1 -SboxRoot "D:\SteamLibrary\steamapps\common\sbox"
```

To configure selected clients only:

```powershell
.\install.ps1 -Clients Codex,Cursor
```

The installer backs up existing client configurations, generates a
cryptographically random `SBOX_MCP_API_KEY`, and stores it in the user
environment rather than in committed configuration files.

## Client configuration

Ready-to-copy examples are under [`clients`](clients).

| Client | Configuration path |
| --- | --- |
| Codex | `%USERPROFILE%\.codex\config.toml` |
| Cursor | `%USERPROFILE%\.cursor\mcp.json` |
| Windsurf | `%USERPROFILE%\.codeium\windsurf\mcp_config.json` |
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` |

Codex, Cursor, and Windsurf connect directly to
`http://localhost:29017/mcp`. Claude Desktop uses the packaged,
self-contained `SboxMcp.StdioBridge.exe`. The bridge refuses non-loopback URLs.

Restart GUI applications after changing user environment variables.

## Server configuration

Configuration layers are merged in this order, with later layers winning:

1. Safe code defaults
2. `Editor/mcp_config.json`
3. Project-root `mcp_config.json`
4. `%APPDATA%\sbox-mcp\mcp_config.json`
5. `SBOX_MCP_API_KEY` and `SBOX_MCP_PORT`

Important settings include `Port`, `MaxRequestsPerSecond`,
`RequestTimeoutMs`, `CorsOrigins`, `EnableConsoleCommands`,
`ScreenshotDirectory`, `ScreenshotDirectoryBase`, `ConfigVersion`,
`ApprovalMode`, approval limits, and history retention settings.

Malformed layers are skipped with a warning. Invalid ports, limits, and
timeouts return to safe defaults. Unversioned v1 files are backed up and
atomically migrated to schema v2. A newer unsupported schema prevents server
startup.

## Tools and undo behavior

Query tools:

- `get_scene_hierarchy`, `list_objects`, `get_selection`
- `get_object_properties`

Mutation tools:

- `create_object`, `delete_object`, `update_object_properties`
- `duplicate_object` duplicates the complete subtree and components
- `set_parent` preserves the world transform by default
- `add_component`, `remove_component`
- `instantiate_prefab`, `break_prefab`
- `undo_scene_change`, `redo_scene_change`
- `submit_scene_changes`, `get_change_request`, `cancel_change_request`
- `get_server_status`

Editor utilities:

- `focus_camera_on_object`, `capture_viewport_screenshot`
- `execute_console_command` when explicitly enabled
- `list_tools`

Scene mutations return `pending_approval` and a `requestId` by default. Review
them in **Editor → MCP Server → Control Center**. The AI can poll
`get_change_request` for the result. `submit_scene_changes` groups up to 50
mutations into one approval and one `[MCP]` undo entry.

Each successful scene mutation produces one `[MCP] ...` history entry. The AI
undo tool refuses to undo a newer manual editor action. Normal Ctrl+Z/Ctrl+Y
remains available in the editor. Prefab paths must be project-relative
`.prefab` asset paths using `/` separators.

Example prompts:

- “List all objects in the scene and summarize the lights.”
- “Create an object named SpawnPoint at 0, 0, 64.”
- “Move that object to 128, 0, 64.”
- “Duplicate this object with its children and parent the copy to Environment.”
- “Add a BoxCollider component to this object.”
- “Instantiate prefabs/props/crate.prefab in the scene.”
- “Undo your latest scene change.”
- “Capture the viewport and return the saved path.”

## Security

- Do not expose this server beyond `localhost`.
- Never commit or include the API key in screenshots or bug reports.
- Avoid wildcard CORS origins.
- Enable console commands only for trusted local use.
- Keep client-side write/tool approvals enabled.
- Verify release archives with their `.sha256` files.

## Troubleshooting

**401 Unauthorized:** Confirm `SBOX_MCP_API_KEY` is visible to both the s&box
and client processes, then restart both applications.

**Tools are missing:** Check `/health`, the configured endpoint, header name,
and the JSON/TOML syntax.

**Claude closes the connection:** Verify the absolute bridge executable path
and fully quit Claude from the system tray before restarting it.

**Port conflict:** Change `SBOX_MCP_PORT` and every client endpoint to the same
unused port.

**URL access denied:** Reserve only the selected loopback URL from an elevated
PowerShell:

```powershell
netsh http add urlacl url=http://localhost:29017/ user="$env:USERDOMAIN\$env:USERNAME"
```

**Screenshot failure:** Check the configured base directory and write
permissions. Requested filenames cannot contain directories or traversal.

## Development and release

Requirements: Windows, s&box Editor, and the .NET 10 SDK.

```powershell
dotnet test .\mcp_server.slnx -p:StaticWebAssetsEnabled=false
.\scripts\build-release.ps1 -Version 1.2.0
```

The release ZIP and SHA-256 checksum are written to `dist`.

### GitHub automation

- `CI` validates repository files, tests the bridge, and verifies a
  self-contained publish on a GitHub-hosted Windows runner.
- `s&box Full Validation` runs all editor tests manually on a
  `self-hosted`, `Windows`, `sbox` runner with s&box installed.
- `Release` creates the ZIP, checksum, and GitHub Release for tags such as
  `v1.2.0`.
- Dependabot checks NuGet and GitHub Actions dependencies monthly.

Use the portable MSBuild property for a non-default s&box installation:

```powershell
dotnet test .\mcp_server.slnx `
  -p:StaticWebAssetsEnabled=false `
  -p:SboxRoot="D:\SteamLibrary\steamapps\common\sbox"
```

## Uninstall

```powershell
.\uninstall.ps1
```

To also remove the user API key:

```powershell
.\uninstall.ps1 -RemoveApiKey
```
