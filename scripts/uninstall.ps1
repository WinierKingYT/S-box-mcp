[CmdletBinding()]
param(
	[switch]$RemoveApiKey
)

$ErrorActionPreference = "Stop"
$manifestPath = Join-Path $env:LOCALAPPDATA "sbox-mcp\install.json"
if (-not (Test-Path -LiteralPath $manifestPath)) {
	throw "Installation manifest not found: $manifestPath"
}
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json

function Remove-ExactTree {
	param(
		[Parameter(Mandatory)][string]$Path,
		[Parameter(Mandatory)][string]$ExpectedParent
	)
	$resolvedPath = [IO.Path]::GetFullPath($Path)
	$resolvedParent = [IO.Path]::GetFullPath($ExpectedParent).TrimEnd('\') + '\'
	if (-not $resolvedPath.StartsWith($resolvedParent, [StringComparison]::OrdinalIgnoreCase)) {
		throw "Refusing to remove a path outside the expected parent: $resolvedPath"
	}
	if (Test-Path -LiteralPath $resolvedPath) {
		Remove-Item -LiteralPath $resolvedPath -Recurse -Force
		Write-Host "Removed: $resolvedPath"
	}
}

function Remove-JsonMcpEntry {
	param([Parameter(Mandatory)][string]$Path)
	if (-not (Test-Path -LiteralPath $Path)) { return }
	try {
		$config = Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
	}
	catch {
		Write-Warning "Invalid JSON; leaving client config unchanged: $Path"
		return
	}
	if ($null -eq $config.mcpServers -or $null -eq $config.mcpServers.sbox) { return }
	Copy-Item -LiteralPath $Path -Destination "$Path.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
	$config.mcpServers.PSObject.Properties.Remove("sbox")
	$config | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Path -Encoding utf8
	Write-Host "Removed sbox entry from: $Path"
}

$addonPath = [IO.Path]::GetFullPath([string]$manifest.addonPath)
$expectedAddonSuffix = "\addons\mcp_server"
if (-not $addonPath.EndsWith($expectedAddonSuffix, [StringComparison]::OrdinalIgnoreCase)) {
	throw "Installation manifest contains an unexpected addon path: $addonPath"
}
$expectedAddonsRoot = [IO.Path]::GetFullPath(
	(Join-Path ([string]$manifest.sboxRoot) "addons")
)
Remove-ExactTree -Path $addonPath -ExpectedParent $expectedAddonsRoot
$bridgeRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "sbox-mcp"))
Remove-ExactTree -Path ([string]$manifest.bridgePath) -ExpectedParent $bridgeRoot

$homeDirectory = [Environment]::GetFolderPath("UserProfile")
Remove-JsonMcpEntry -Path (Join-Path $homeDirectory ".cursor\mcp.json")
Remove-JsonMcpEntry -Path (Join-Path $homeDirectory ".codeium\windsurf\mcp_config.json")
Remove-JsonMcpEntry -Path (Join-Path $env:APPDATA "Claude\claude_desktop_config.json")

$codexPath = Join-Path $homeDirectory ".codex\config.toml"
if (Test-Path -LiteralPath $codexPath) {
	$content = Get-Content -Raw -LiteralPath $codexPath
	$pattern = '(?ms)\r?\n?\[mcp_servers\.sbox\]\r?\n.*?(?=\r?\n\[|\z)'
	$updated = [regex]::Replace($content, $pattern, "").TrimEnd() + [Environment]::NewLine
	if ($updated -ne $content) {
		Copy-Item -LiteralPath $codexPath -Destination "$codexPath.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
		Set-Content -LiteralPath $codexPath -Value $updated -Encoding utf8
		Write-Host "Removed sbox entry from: $codexPath"
	}
}

if ($RemoveApiKey) {
	[Environment]::SetEnvironmentVariable("SBOX_MCP_API_KEY", $null, "User")
	[Environment]::SetEnvironmentVariable("SBOX_MCP_PORT", $null, "User")
	Write-Host "Removed SBOX_MCP_API_KEY and SBOX_MCP_PORT user variables."
}

Remove-Item -LiteralPath $manifestPath -Force
Write-Host "s&box MCP uninstalled. Restart affected applications."
