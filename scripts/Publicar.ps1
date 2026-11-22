param([string]$Destination, [switch]$SoloAutocontenida)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sdk = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if ($sdk) { $sdk.Source } else { "$env:LOCALAPPDATA/AugmentLens/dotnet/dotnet.exe" }
if (!(Test-Path $dotnet)) { throw 'Instala el SDK de .NET 10.' }

& "$PSScriptRoot/Get-Assets.ps1" -SkipCatalog
if (!$Destination) { $Destination = Join-Path $projectRoot 'dist/win-x64' }
$project = "$projectRoot/src/Aumentos.App/Aumentos.App.csproj"
& $dotnet publish $project -c Release -r win-x64 --self-contained true -o $destination
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($SoloAutocontenida) { return }
& $dotnet publish $project -c Release --no-self-contained -o (Join-Path $destination 'framework')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -LiteralPath "$PSScriptRoot/Iniciar.cmd" -Destination $destination
