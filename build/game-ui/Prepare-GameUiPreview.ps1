# Stage a verified candidate in a development client and create explicit preview/Legacy launchers.
# Does not start the client, edit INI, change resolution, or enable the production release gate.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ClientDirectory,
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GameUiReleaseTools.ps1')
$clientRoot = Get-GameUiFullPath $ClientDirectory
$source = Get-GameUiFullPath $PackagePath
foreach ($path in @($clientRoot, $source)) { Assert-GameUiOrdinaryPath $path }
if (Test-GameUiWithin $clientRoot 'E:\Program Files\ShengquGames\Mir3') {
    throw 'The official game installation is read-only; choose the project client output.'
}
foreach ($name in @('Zircon.exe', 'Library.dll', 'SlimDX.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $clientRoot $name) -PathType Leaf)) { throw "Missing client runtime: $name" }
}
if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing candidate package: $source" }
$launchers = [ordered]@{
    'Start-OfficialUiPreview.cmd' = "@echo off`r`ncd /d `"%~dp0`"`r`nstart `"`" `"%~dp0Zircon.exe`" --game-ui-preview`r`n"
    'Start-LegacyUi.cmd' = "@echo off`r`ncd /d `"%~dp0`"`r`nstart `"`" `"%~dp0Zircon.exe`" --game-ui-legacy`r`n"
}
# Keep the inspected source immutable until copying and post-copy verification finish.
$lease = [IO.File]::Open($source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    $contract = Invoke-GameUiContractInspection $clientRoot $source
    $target = Join-Path $clientRoot $contract.packageRelativePath
    Assert-GameUiOrdinaryPath $target
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -cne $contract.packageSha256) { throw 'Source package changed after inspection.' }
    if (Test-Path -LiteralPath $target) {
        if (!(Test-Path -LiteralPath $target -PathType Leaf) -or
            (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -cne $contract.packageSha256) {
            throw 'A different package already exists in the client; it was not overwritten.'
        }
    }
    foreach ($name in $launchers.Keys) {
        $path = Join-Path $clientRoot $name
        Assert-GameUiOrdinaryPath $path
        if ((Test-Path -LiteralPath $path) -and (!(Test-Path -LiteralPath $path -PathType Leaf) -or
            [IO.File]::ReadAllText($path) -cne $launchers[$name])) {
            throw "A different launcher already exists; it was not overwritten: $path"
        }
    }
    if (!$ValidateOnly) {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        if (!(Test-Path -LiteralPath $target)) {
            $stream = [IO.File]::Open($target, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try {
                try { $lease.Position = 0; $lease.CopyTo($stream) } finally { $stream.Dispose() }
            } catch {
                # Only remove the incomplete file created by this invocation.
                Remove-Item -LiteralPath $target -Force
                throw
            }
        }
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -cne $contract.packageSha256) { throw 'Staged package differs from compiled catalog.' }
        foreach ($name in $launchers.Keys) {
            $path = Join-Path $clientRoot $name
            if (!(Test-Path -LiteralPath $path)) {
                $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                try { $bytes = [Text.Encoding]::ASCII.GetBytes($launchers[$name]); $stream.Write($bytes, 0, $bytes.Length) }
                finally { $stream.Dispose() }
            }
        }
    }
    [pscustomobject][ordered]@{
        clientDirectory = $clientRoot; validatedOnly = [bool]$ValidateOnly
        packageSha256 = $contract.packageSha256; frameCount = @($contract.frames).Count
        runtimeReady = $contract.runtimeReady
        previewLauncher = (Join-Path $clientRoot 'Start-OfficialUiPreview.cmd')
        legacyLauncher = (Join-Path $clientRoot 'Start-LegacyUi.cmd')
    }
} finally { $lease.Dispose() }
