$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (!$compiler) {
    $compiler = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe"
        "$env:ProgramFiles\Inno Setup 7\ISCC.exe"
        "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (!$compiler) { throw 'Instala Inno Setup 7 con winget install --id JRSoftware.InnoSetup.7 -e --scope user.' }

$dist = [IO.Directory]::CreateDirectory((Join-Path $projectRoot 'dist'))
$work = New-Item -ItemType Directory -Path (Join-Path $dist.FullName ("instalador-" + [guid]::NewGuid().ToString('N')))
try {
    $publish = Join-Path $work.FullName 'app'
    & "$PSScriptRoot/Publicar.ps1" -Destination $publish -SoloAutocontenida
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish termino con codigo $LASTEXITCODE." }

    # DLL redistribuibles x64 que importan Tesseract y Leptonica (Visual Studio 2022).
    $sha256 = '4aaf54db0bfc9435f7c3660e1a00237a4b556042bfeea64bde44c2e0194e6ee5'
    $url = "https://download.visualstudio.microsoft.com/download/pr/45d3b8dd-bced-4b37-9974-142f748d710c/$sha256/Microsoft.VC.14.44.17.14.CRT.Redist.X64.base.vsix"
    $redist = Join-Path $work.FullName 'vc-redist.vsix'
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $redist -TimeoutSec 90
    if ((Get-FileHash -LiteralPath $redist -Algorithm SHA256).Hash -ne $sha256) {
        throw 'El paquete de Visual C++ descargado no coincide con el SHA-256 esperado.'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($redist)
    try {
        $crt = 'Contents/VC/Redist/MSVC/14.44.35112/x64/Microsoft.VC143.CRT'
        foreach ($name in @('msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll')) {
            $entry = $archive.GetEntry("$crt/$name")
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $publish $name))
        }
    } finally { $archive.Dispose() }

    & $compiler /Q "/DPublishDir=$publish" "/O$($work.FullName)" "$PSScriptRoot/Instalador.iss"
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup termino con codigo $LASTEXITCODE." }
    $name = 'Instalar-AumentosLoL-x64.exe'
    $outputFile = Join-Path $dist.FullName $name
    Move-Item -LiteralPath (Join-Path $work.FullName $name) -Destination $outputFile -Force
    Write-Output $outputFile
} finally {
    $workPath = (Resolve-Path -LiteralPath $work.FullName).Path
    if ((Split-Path -Parent $workPath) -eq $dist.FullName -and $work.Name -match '^instalador-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $workPath -Recurse -Force
    }
}
