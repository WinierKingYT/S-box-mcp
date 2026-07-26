[CmdletBinding()]
param(
	[string]$Version = "1.2.0",
	[string]$Runtime = "win-x64",
	[switch]$SkipSboxTests
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$distRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "dist"))
$stageRoot = [IO.Path]::GetFullPath((Join-Path $distRoot "stage-$Version-$Runtime"))
if (-not $stageRoot.StartsWith($distRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
	throw "Invalid staging path."
}

Push-Location $repositoryRoot
try {
	& ".\scripts\validate-repository.ps1"
	if ($SkipSboxTests) {
		dotnet test "tools\SboxMcp.StdioBridge.Tests\SboxMcp.StdioBridge.Tests.csproj"
	}
	else {
		dotnet test "mcp_server.slnx" -p:StaticWebAssetsEnabled=false
	}
	if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

	if (Test-Path -LiteralPath $stageRoot) {
		Remove-Item -LiteralPath $stageRoot -Recurse -Force
	}
	New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null
	$addonRoot = Join-Path $stageRoot "addon"
	New-Item -ItemType Directory -Force -Path $addonRoot | Out-Null

	Copy-Item -LiteralPath "mcp_server.sbproj" -Destination $addonRoot
	Copy-Item -LiteralPath "mcp_server.sboxproj" -Destination $addonRoot
	Copy-Item -LiteralPath "ProjectSettings" -Destination $addonRoot -Recurse
	Copy-Item -LiteralPath "code" -Destination $addonRoot -Recurse
	Copy-Item -LiteralPath "Editor" -Destination $addonRoot -Recurse
	$excluded = @(
		(Join-Path $addonRoot "Editor\unittest"),
		(Join-Path $addonRoot "Editor\bin"),
		(Join-Path $addonRoot "Editor\obj"),
		(Join-Path $addonRoot "Editor\Properties"),
		(Join-Path $addonRoot "code\bin"),
		(Join-Path $addonRoot "code\obj"),
		(Join-Path $addonRoot "code\Properties")
	)
	foreach ($path in $excluded) {
		if (Test-Path -LiteralPath $path) {
			Remove-Item -LiteralPath $path -Recurse -Force
		}
	}

	$bridgeRoot = Join-Path $stageRoot "bridge"
	dotnet publish "tools\SboxMcp.StdioBridge\SboxMcp.StdioBridge.csproj" `
		-c Release -r $Runtime --self-contained true `
		-p:PublishSingleFile=true -p:Version=$Version -o $bridgeRoot
	if ($LASTEXITCODE -ne 0) { throw "Bridge publish failed." }
	Get-ChildItem -LiteralPath $bridgeRoot -Filter "*.pdb" |
		Remove-Item -Force

	Copy-Item -LiteralPath "clients" -Destination $stageRoot -Recurse
	Copy-Item -LiteralPath "README.md" -Destination $stageRoot
	Copy-Item -LiteralPath "README.en.md" -Destination $stageRoot
	Copy-Item -LiteralPath "scripts\install.ps1" -Destination $stageRoot
	Copy-Item -LiteralPath "scripts\uninstall.ps1" -Destination $stageRoot
	[pscustomobject]@{
		name = "sbox-mcp"
		version = $Version
		runtime = $Runtime
	} | ConvertTo-Json | Set-Content `
		-LiteralPath (Join-Path $stageRoot "release.json") -Encoding utf8

	$archivePath = Join-Path $distRoot "sbox-mcp-$Version-$Runtime.zip"
	if (Test-Path -LiteralPath $archivePath) {
		Remove-Item -LiteralPath $archivePath -Force
	}
	Compress-Archive -Path (Join-Path $stageRoot "*") -DestinationPath $archivePath
	$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
	Set-Content -LiteralPath "$archivePath.sha256" `
		-Value "$hash  $(Split-Path -Leaf $archivePath)" -Encoding ascii
	Write-Host "Release: $archivePath"
	Write-Host "SHA-256: $hash"
}
finally {
	Pop-Location
}
