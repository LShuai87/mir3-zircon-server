[CmdletBinding()]
param(
    [ValidateSet('Reference','Runtime')][string]$Mode = 'Reference',
    [Parameter(Mandatory = $true)][string]$SourcePath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$BaselinePath = (Join-Path $PSScriptRoot 'source-libraries.json'),
    [string]$PythonPath = 'python',
    [string]$SelectionPath,
    [string]$ImageManagerPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Mode -eq 'Runtime') {
    throw 'Runtime packaging is unavailable until G0-1..G0-5, semantic roles and layouts are frozen. Reference packages cannot be deployed as OfficialV1.Zl.'
}
function Get-FullPath([string]$Value) {
    $full = [IO.Path]::GetFullPath($Value)
    if ($full -eq [IO.Path]::GetPathRoot($full)) { return $full }
    return $full.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}
function Test-Within([string]$Child, [string]$Parent) {
    $prefix = $Parent.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return $Child.Equals($Parent, [StringComparison]::OrdinalIgnoreCase) -or $Child.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}
function Assert-OrdinaryPath([string]$Value) {
    for ($current = $Value; $current; $current = [IO.Path]::GetDirectoryName($current)) {
        if (Test-Path -LiteralPath $current) {
            if (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse points are not supported in build paths: $current"
            }
        }
    }
}
$source = Get-FullPath $SourcePath
$output = Get-FullPath $OutputPath
$baseline = Get-FullPath $BaselinePath
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (!$ImageManagerPath) { $ImageManagerPath = Join-Path (Split-Path -Parent $repository) 'Debug\ImageManager\ImageManager.exe' }
$builder = Get-FullPath $ImageManagerPath
$strictDecoder = Join-Path $PSScriptRoot 'Decode-OfficialUi.py'
$decoder = $strictDecoder
$selection = $null
if ($SelectionPath) {
    $selection = Get-FullPath $SelectionPath
    $decoder = Join-Path $PSScriptRoot 'Select-OfficialUi.py'
}
$inputs = @($baseline, $builder, $decoder, $strictDecoder)
if ($selection) { $inputs += $selection }
if (!(Test-Path -LiteralPath $source -PathType Container)) { throw "Source Data directory does not exist: $source" }
if ((Test-Within $source $output) -or (Test-Within $output $source)) { throw 'Source and output directories must be disjoint.' }
if (Test-Path -LiteralPath $output) { throw "Output must be a new directory: $output" }
foreach ($item in $inputs) {
    if (!(Test-Path -LiteralPath $item -PathType Leaf)) { throw "Required build input does not exist: $item" }
    if (Test-Within $item $output) { throw 'Build inputs must be outside the output directory.' }
}
foreach ($item in (@($source, $output) + $inputs)) { Assert-OrdinaryPath $item }
& $PythonPath -c 'import sys; from PIL import Image; assert sys.version_info >= (3,10), "Python 3.10+ required"'
if ($LASTEXITCODE -ne 0) { throw 'Python 3.10+ and Pillow are required.' }
$parent = [IO.Path]::GetDirectoryName($output)
[IO.Directory]::CreateDirectory($parent) | Out-Null
$staging = Join-Path $parent ('.' + [IO.Path]::GetFileName($output) + '.staging-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($staging) | Out-Null
try {
    $decoded = Join-Path $staging 'Decoded'
    $package = Join-Path $staging 'Package'
    $decodeArguments = @('--source', $source, '--output', $decoded, '--baseline', $baseline)
    if ($selection) { $decodeArguments += @('--selection', $selection) }
    & $PythonPath $decoder @decodeArguments
    if ($LASTEXITCODE -ne 0) { throw 'Official UI decoding failed; no package was published.' }
    # ImageManager is a GUI executable: direct invocation can return before conversion finishes.
    $builderArguments = @('--official-ui', 'reference', ('"' + $decoded + '"'), ('"' + $package + '"'), ('"' + (Join-Path $decoded 'manifest.json') + '"'))
    $process = Start-Process -FilePath $builder -ArgumentList $builderArguments -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $staging 'builder.stdout.log') -RedirectStandardError (Join-Path $staging 'builder.stderr.log')
    if ($process.ExitCode -ne 0) {
        $detail = [IO.File]::ReadAllText((Join-Path $staging 'builder.stderr.log'))
        throw "Reference Zl conversion failed (exit $($process.ExitCode)): $detail"
    }
    $referenceManifest = Join-Path $package 'reference-manifest.json'
    $tools = @($inputs + $PSCommandPath | Select-Object -Unique)
    $tools += @(Get-ChildItem -LiteralPath ([IO.Path]::GetDirectoryName($builder)) -File | Where-Object { $_.Extension -eq '.dll' -or $_.Name -eq 'ImageManager.exe.config' } | Sort-Object Name | ForEach-Object { $_.FullName })
    $toolHashes = @($tools | ForEach-Object { [ordered]@{ name = [IO.Path]::GetFileName($_); sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() } })
    $report = [ordered]@{
        schemaVersion = 1
        kind = 'official-ui-reference-build'
        runtimeReady = $false
        selectedOnly = [bool]$selection
        alphaVerified = $false
        decodedManifest = 'Decoded/manifest.json'
        decodedManifestSha256 = (Get-FileHash -LiteralPath (Join-Path $decoded 'manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()
        referenceManifest = 'Package/reference-manifest.json'
        referenceManifestSha256 = (Get-FileHash -LiteralPath $referenceManifest -Algorithm SHA256).Hash.ToLowerInvariant()
        tools = $toolHashes
    }
    [IO.File]::WriteAllText((Join-Path $staging 'build.json'), (($report | ConvertTo-Json -Depth 8) + [Environment]::NewLine), (New-Object Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $output) { throw 'Output appeared during conversion; refusing to replace it.' }
    [IO.Directory]::Move($staging, $output)
    Write-Output "Reference conversion complete: $output"
    Write-Output 'Opaque reference libraries are for asset selection. Final semantic roles and alpha rules remain unverified.'
} finally {
    if (Test-Path -LiteralPath $staging) {
        $cleanup = Get-FullPath $staging
        if (!(Test-Within $cleanup $parent) -or $cleanup -eq $parent -or $cleanup -eq $output -or (Test-Within $cleanup $source)) {
            throw "Refusing unsafe staging cleanup: $cleanup"
        }
        Assert-OrdinaryPath $cleanup
        Remove-Item -LiteralPath $cleanup -Recurse -Force
    }
}
