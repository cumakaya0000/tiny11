$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $repoRoot 'src\NexusPC.Desktop\NexusPC.Desktop.csproj'
$publishDir = Join-Path $repoRoot 'artifacts\publish\win-x64'
$installerDir = Join-Path $repoRoot 'artifacts\installer'
$scriptPath = Join-Path $repoRoot 'installer\NexusPC.iss'

dotnet publish $project -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -p:PublishTrimmed=false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'NEXUS PC publish failed.' }

$isccCommand = Get-Command iscc.exe -ErrorAction SilentlyContinue
$iscc = if ($isccCommand) { $isccCommand.Source } else { $null }
if (-not $iscc) {
  $candidate = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'
  if (Test-Path -LiteralPath $candidate) { $iscc = $candidate }
}
if (-not $iscc) { throw 'Inno Setup 7 compiler (ISCC.exe) was not found. Install it, then rerun this script.' }

New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
& $iscc $scriptPath
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
Write-Host "Installer created in $installerDir"
