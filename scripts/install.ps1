[CmdletBinding()]
param(
	[string]$SboxRoot,
	[int]$Port = 29017,
	[string[]]$Clients = @("Codex", "ClaudeDesktop", "Cursor", "Windsurf")
)

$ErrorActionPreference = "Stop"

function Backup-File {
	param([Parameter(Mandatory)][string]$Path)
	if (Test-Path -LiteralPath $Path) {
		$backup = "$Path.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
		Copy-Item -LiteralPath $Path -Destination $backup
		Write-Host "Backup: $backup"
	}
}

function Set-JsonMcpEntry {
	param(
		[Parameter(Mandatory)][string]$Path,
		[Parameter(Mandatory)][object]$Entry
	)
	$directory = Split-Path -Parent $Path
	New-Item -ItemType Directory -Force -Path $directory | Out-Null
	Backup-File -Path $Path
	if (Test-Path -LiteralPath $Path) {
		try {
			$config = Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
		}
		catch {
			Write-Warning "Invalid JSON; leaving client config unchanged: $Path"
			return
		}
	}
	else {
		$config = [pscustomobject]@{}
	}
	if ($null -eq $config.mcpServers) {
		$config | Add-Member -MemberType NoteProperty -Name mcpServers -Value ([pscustomobject]@{})
	}
	$config.mcpServers | Add-Member -MemberType NoteProperty -Name sbox -Value $Entry -Force
	$config | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Path -Encoding utf8
	Write-Host "Configured: $Path"
}

if ($Port -le 0 -or $Port -gt 65535) {
	throw "Port must be between 1 and 65535."
}

if ([string]::IsNullOrWhiteSpace($SboxRoot)) {
	$programFilesX86 = [Environment]::GetFolderPath("ProgramFilesX86")
	$SboxRoot = Join-Path $programFilesX86 "Steam\steamapps\common\sbox"
}
$SboxRoot = [IO.Path]::GetFullPath($SboxRoot)
if (-not (Test-Path -LiteralPath $SboxRoot -PathType Container)) {
	throw "s&box directory not found. Pass -SboxRoot explicitly: $SboxRoot"
}

$addonSource = Join-Path $PSScriptRoot "addon"
$bridgeSource = Join-Path $PSScriptRoot "bridge"
if (-not (Test-Path -LiteralPath $addonSource -PathType Container)) {
	throw "Release package is incomplete: addon directory is missing."
}
if (-not (Test-Path -LiteralPath $bridgeSource -PathType Container)) {
	throw "Release package is incomplete: bridge directory is missing."
}

$addonsRoot = [IO.Path]::GetFullPath((Join-Path $SboxRoot "addons"))
$addonDestination = [IO.Path]::GetFullPath((Join-Path $addonsRoot "mcp_server"))
if (-not $addonDestination.StartsWith(
		$addonsRoot.TrimEnd('\') + '\',
		[StringComparison]::OrdinalIgnoreCase)) {
	throw "Resolved addon destination is outside the s&box addons directory."
}
New-Item -ItemType Directory -Force -Path $addonsRoot | Out-Null
if (Test-Path -LiteralPath $addonDestination) {
	$backupDestination = "$addonDestination.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
	Move-Item -LiteralPath $addonDestination -Destination $backupDestination
	Write-Host "Existing addon moved to: $backupDestination"
}
Copy-Item -LiteralPath $addonSource -Destination $addonDestination -Recurse

$bridgeDestination = Join-Path $env:LOCALAPPDATA "sbox-mcp\bridge"
New-Item -ItemType Directory -Force -Path $bridgeDestination | Out-Null
Copy-Item -Path (Join-Path $bridgeSource "*") -Destination $bridgeDestination -Recurse -Force
$bridgeExecutable = Join-Path $bridgeDestination "SboxMcp.StdioBridge.exe"
$releaseVersion = "unknown"
$releaseManifestPath = Join-Path $PSScriptRoot "release.json"
if (Test-Path -LiteralPath $releaseManifestPath) {
	$releaseVersion = (
		Get-Content -Raw -LiteralPath $releaseManifestPath |
		ConvertFrom-Json
	).version
}

$apiKey = [Environment]::GetEnvironmentVariable("SBOX_MCP_API_KEY", "User")
if ([string]::IsNullOrWhiteSpace($apiKey)) {
	$bytes = New-Object byte[] 32
	$random = [Security.Cryptography.RandomNumberGenerator]::Create()
	try {
		$random.GetBytes($bytes)
	}
	finally {
		$random.Dispose()
	}
	$apiKey = ($bytes | ForEach-Object { $_.ToString("x2") }) -join ""
	[Environment]::SetEnvironmentVariable("SBOX_MCP_API_KEY", $apiKey, "User")
	Write-Host "Created user environment variable: SBOX_MCP_API_KEY"
}
[Environment]::SetEnvironmentVariable("SBOX_MCP_PORT", $Port.ToString(), "User")

$endpoint = "http://localhost:$Port/mcp"
$homeDirectory = [Environment]::GetFolderPath("UserProfile")

if ($Clients -contains "Codex") {
	$codexPath = Join-Path $homeDirectory ".codex\config.toml"
	$codexBlock = @"

[mcp_servers.sbox]
url = "$endpoint"
env_http_headers = { "x-api-key" = "SBOX_MCP_API_KEY" }
required = true
startup_timeout_sec = 20
tool_timeout_sec = 35
"@
	New-Item -ItemType Directory -Force -Path (Split-Path -Parent $codexPath) | Out-Null
	if (Test-Path -LiteralPath $codexPath) {
		$content = Get-Content -Raw -LiteralPath $codexPath
		if ($content -match '(?m)^\[mcp_servers\.sbox\]\s*$') {
			Write-Warning "Codex already has mcp_servers.sbox; leaving it unchanged."
		}
		else {
			Backup-File -Path $codexPath
			Add-Content -LiteralPath $codexPath -Value $codexBlock -Encoding utf8
			Write-Host "Configured: $codexPath"
		}
	}
	else {
		Set-Content -LiteralPath $codexPath -Value $codexBlock.TrimStart() -Encoding utf8
		Write-Host "Configured: $codexPath"
	}
}

if ($Clients -contains "Cursor") {
	Set-JsonMcpEntry -Path (Join-Path $homeDirectory ".cursor\mcp.json") -Entry ([pscustomobject]@{
		url = $endpoint
		headers = [pscustomobject]@{ "x-api-key" = '${env:SBOX_MCP_API_KEY}' }
	})
}

if ($Clients -contains "Windsurf") {
	Set-JsonMcpEntry -Path (Join-Path $homeDirectory ".codeium\windsurf\mcp_config.json") -Entry ([pscustomobject]@{
		serverUrl = $endpoint
		headers = [pscustomobject]@{ "x-api-key" = '${env:SBOX_MCP_API_KEY}' }
	})
}

if ($Clients -contains "ClaudeDesktop") {
	Set-JsonMcpEntry -Path (Join-Path $env:APPDATA "Claude\claude_desktop_config.json") -Entry ([pscustomobject]@{
		command = $bridgeExecutable
		args = @("--url", $endpoint, "--api-key-env", "SBOX_MCP_API_KEY")
	})
}

$manifest = [pscustomobject]@{
	version = $releaseVersion
	sboxRoot = $SboxRoot
	addonPath = $addonDestination
	bridgePath = $bridgeDestination
	port = $Port
	installedAt = [DateTimeOffset]::Now.ToString("O")
}
$manifestPath = Join-Path $env:LOCALAPPDATA "sbox-mcp\install.json"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $manifestPath) | Out-Null
$manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Host ""
Write-Host "s&box MCP installed successfully."
Write-Host "Restart s&box and all configured AI clients so they receive the environment variables."
Write-Host "Health endpoint: http://localhost:$Port/health"
