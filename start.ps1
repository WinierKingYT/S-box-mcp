param(
    [string]$ProjectDir = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

function Write-Step($msg) {
    Write-Host "`n=== $msg ===" -ForegroundColor Cyan
}

# Check prerequisites
$sboxDir = "C:\Program Files (x86)\Steam\steamapps\common\sbox"
if (-not (Test-Path $sboxDir)) {
    Write-Host "s&box not found at $sboxDir. Install from Steam first." -ForegroundColor Red
    exit 1
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Host ".NET SDK not found. Install .NET 10 SDK." -ForegroundColor Red
    exit 1
}

# Build game
Write-Step "Building game"
dotnet build "$ProjectDir\Code\blackfriday2.csproj" --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "Game build failed!" -ForegroundColor Red
    exit 1
}

# Open s&box
$sboxExe = "$sboxDir\sbox.exe"
Write-Host @"

  Build OK. Open the project in s&box and:
  1. Editor -> MCP Server -> Start Server (port 29016 SSE)
  2. Press Play to activate game tools
  3. Connect AI tool to http://localhost:29016/sse

"@ -ForegroundColor Green

Start-Process $sboxExe -ArgumentList "-launch `"$ProjectDir`""
