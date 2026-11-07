# Prepares x86 native runtime dependencies beside Zircon.exe; does not install system components.
[CmdletBinding()]
param(
    [string[]]$OutputDirectories,
    [string]$CacheDirectory = (Join-Path $env:LOCALAPPDATA 'Zircon\NativeRuntime\x86'),
    [string]$SevenZipPath
)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'client-runtime.json') -Raw | ConvertFrom-Json
if (!$OutputDirectories) {
    $workspaceParent = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $OutputDirectories = @((Join-Path $workspaceParent 'Debug\Client'), (Join-Path $workspaceParent 'Release\Client'))
}
function Test-Hash([string]$Path, [string]$Expected) {
    return (Test-Path -LiteralPath $Path -PathType Leaf) -and ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Expected)
}
function Expand-ArchiveFile([string]$Archive, [string]$Destination) {
    & $script:SevenZipPath x -y ('-o' + $Destination) $Archive | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "7-Zip failed to extract $Archive (exit $LASTEXITCODE)" }
}
$missing = @($manifest.files.PSObject.Properties | Where-Object { !(Test-Hash (Join-Path $CacheDirectory $_.Name) $_.Value) })
if ($missing.Count -gt 0) {
    if (!$SevenZipPath) {
        $zipCommand = Get-Command 7z.exe -ErrorAction SilentlyContinue
        if ($zipCommand) { $SevenZipPath = $zipCommand.Source }
        else {
            $SevenZipPath = @('C:\Program Files\7-Zip\7z.exe', (Join-Path $env:ProgramFiles '7-Zip\7z.exe')) |
                Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        }
    }
    if (!$SevenZipPath -or !(Test-Path -LiteralPath $SevenZipPath)) { throw '7-Zip is required for extraction. Pass -SevenZipPath with the full path to 7z.exe.' }
    $packageDirectory = Join-Path $CacheDirectory 'packages'
    [System.IO.Directory]::CreateDirectory($packageDirectory) | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    foreach ($source in $manifest.sources) {
        $packagePath = Join-Path $packageDirectory $source.name
        if (!(Test-Hash $packagePath $source.sha256)) {
            Write-Host ('Downloading Microsoft package: ' + $source.name)
            Invoke-WebRequest -UseBasicParsing -Uri $source.url -OutFile $packagePath
        }
        if (!(Test-Hash $packagePath $source.sha256)) { throw "Package SHA256 mismatch: $packagePath" }
        $signature = Get-AuthenticodeSignature -LiteralPath $packagePath
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation(?:,|$)') {
            throw "Microsoft signature verification failed: $packagePath ($($signature.Status))"
        }
    }
    $vcDirectory = Join-Path $CacheDirectory 'vc-extracted'
    $vcFiles = Join-Path $CacheDirectory 'vc-files'
    $dxDirectory = Join-Path $CacheDirectory 'directx-extracted'
    $dxFiles = Join-Path $CacheDirectory 'directx-files'
    Expand-ArchiveFile (Join-Path $packageDirectory 'vcredist_x86.exe') $vcDirectory
    Expand-ArchiveFile (Join-Path $vcDirectory 'vc_red.cab') $vcFiles
    Expand-ArchiveFile (Join-Path $packageDirectory 'directx_Jun2010_redist.exe') $dxDirectory
    foreach ($cab in $manifest.directXCabs) { Expand-ArchiveFile (Join-Path $dxDirectory $cab) $dxFiles }
    $extractedFiles = @{
        'msvcr100.dll' = (Join-Path $vcFiles 'F_CENTRAL_msvcr100_x86')
        'msvcp100.dll' = (Join-Path $vcFiles 'F_CENTRAL_msvcp100_x86')
    }
    foreach ($file in $manifest.files.PSObject.Properties) {
        $sourcePath = if ($extractedFiles.ContainsKey($file.Name)) { $extractedFiles[$file.Name] } else { Join-Path $dxFiles $file.Name }
        if (!(Test-Hash $sourcePath $file.Value)) { throw "Extracted DLL SHA256 mismatch: $sourcePath" }
        Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $CacheDirectory $file.Name) -Force
    }
}
foreach ($directory in $OutputDirectories) {
    if (!(Test-Path -LiteralPath (Join-Path $directory 'Zircon.exe') -PathType Leaf)) { throw "Build Client first; Zircon.exe is missing in $directory" }
    # Prevent accidental deployment into a directory containing a different architecture.
    $slimPath = Join-Path $directory 'SlimDX.dll'
    $stream = [System.IO.File]::OpenRead($slimPath)
    $reader = New-Object System.IO.BinaryReader($stream)
    try {
        $stream.Position = 60
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset + 4
        if ($reader.ReadUInt16() -ne 0x14c) { throw "SlimDX must be x86: $slimPath" }
    } finally { $reader.Dispose() }
    foreach ($file in $manifest.files.PSObject.Properties) {
        $sourcePath = Join-Path $CacheDirectory $file.Name
        if (!(Test-Hash $sourcePath $file.Value)) { throw "Cached DLL SHA256 mismatch: $sourcePath" }
        Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $directory $file.Name) -Force
    }
    Write-Host ("Prepared {0} x86 native DLLs in {1}" -f $manifest.files.PSObject.Properties.Name.Count, $directory)
}
