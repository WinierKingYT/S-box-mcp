[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

function Assert-JsonFile {
	param([Parameter(Mandatory)][string]$Path)
	try {
		Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json | Out-Null
	}
	catch {
		throw "Invalid JSON: $Path`n$($_.Exception.Message)"
	}
}

$jsonFiles = @(
	"Editor\mcp_config.json",
	"mcp_server.sbproj",
	"mcp_server.sboxproj",
	"clients\cursor\mcp.json.example",
	"clients\windsurf\mcp_config.json.example",
	"clients\claude-desktop\claude_desktop_config.json.example"
)
foreach ($relativePath in $jsonFiles) {
	Assert-JsonFile -Path (Join-Path $repositoryRoot $relativePath)
}

$scriptFiles = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "scripts") `
	-Filter "*.ps1" -File
foreach ($scriptFile in $scriptFiles) {
	$tokens = $null
	$errors = $null
	[Management.Automation.Language.Parser]::ParseFile(
		$scriptFile.FullName,
		[ref]$tokens,
		[ref]$errors
	) | Out-Null
	if ($errors.Count -gt 0) {
		throw "Invalid PowerShell: $($scriptFile.FullName)`n$($errors[0].Message)"
	}
}

$serverConfig = Get-Content -Raw -LiteralPath (
	Join-Path $repositoryRoot "Editor\mcp_config.json"
) | ConvertFrom-Json
if (-not [string]::IsNullOrWhiteSpace([string]$serverConfig.ApiKey)) {
	throw "Editor/mcp_config.json must not contain an API key."
}

$clientContent = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot "clients") `
	-Recurse -File |
	ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }
if (($clientContent -join "`n") -match '(?i)\b[a-f0-9]{64}\b') {
	throw "A client template appears to contain a raw 64-character secret."
}

$requiredFiles = @(
	"README.md",
	"README.en.md",
	"SECURITY.md",
	"CONTRIBUTING.md",
	"scripts\install.ps1",
	"scripts\uninstall.ps1",
	"scripts\build-release.ps1"
)
foreach ($relativePath in $requiredFiles) {
	if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath))) {
		throw "Required repository file is missing: $relativePath"
	}
}

Write-Host "Repository validation passed."
