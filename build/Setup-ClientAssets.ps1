# Deploys game assets only. Existing client settings and user databases are preserved.
[CmdletBinding()]
param(
    [string]$ResourceRoot,
    [string[]]$OutputDirectories,
    [ValidateSet('Legacy', 'OfficialV1')][string]$GameUiStyle = 'Legacy',
    [string]$ReleaseManifestPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'game-ui\GameUiReleaseTools.ps1')
if (!$ResourceRoot) { $ResourceRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'data' }
$ResourceRoot = Get-GameUiFullPath (Resolve-Path -LiteralPath $ResourceRoot).Path
Assert-GameUiOrdinaryPath $ResourceRoot
if (!$OutputDirectories) {
    $workspaceParent = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $OutputDirectories = @((Join-Path $workspaceParent 'Debug\Client'))
}
if ($GameUiStyle -eq 'OfficialV1' -and !$ReleaseManifestPath) { throw 'OfficialV1 requires -ReleaseManifestPath.' }
if ($GameUiStyle -eq 'Legacy' -and $ReleaseManifestPath) { throw '-ReleaseManifestPath requires -GameUiStyle OfficialV1.' }
$required = @('Data\Interface1c.Zl', 'Data\GameInter.Zl', 'Data\Interface.Zl', 'Data\Pallete.png', 'Sound\Opening.wav')
if ($GameUiStyle -eq 'OfficialV1') { $required += 'Data\GameUI\OfficialV1.Zl' }
foreach ($relative in $required) {
    if (!(Test-Path -LiteralPath (Join-Path $ResourceRoot $relative) -PathType Leaf)) { throw "Required asset missing: $ResourceRoot\$relative" }
}
$copyPlan = @(
    @{ Directory = 'Data'; Filters = @('*.Zl', '*.png') },
    @{ Directory = 'Map'; Filters = @('*.map', '*.xob') },
    @{ Directory = 'Sound'; Filters = @('*.wav') }
)
$destinations = @($OutputDirectories | ForEach-Object { Get-GameUiFullPath $_ })
foreach ($destination in $destinations) {
    Assert-GameUiOrdinaryPath $destination
    if ((Test-GameUiWithin $destination $ResourceRoot) -or (Test-GameUiWithin $ResourceRoot $destination)) { throw 'Source and destination directories must be disjoint.' }
    if (!(Test-Path -LiteralPath (Join-Path $destination 'Zircon.exe') -PathType Leaf)) { throw "Build Client first; Zircon.exe is missing in $destination" }
    foreach ($other in $destinations) {
        if ($other -cne $destination -and ((Test-GameUiWithin $destination $other) -or (Test-GameUiWithin $other $destination))) { throw 'Output directories must be disjoint.' }
    }
}
foreach ($entry in $copyPlan) {
    $source = Join-Path $ResourceRoot $entry.Directory
    if (!(Test-Path -LiteralPath $source -PathType Container)) { throw "Asset directory missing: $source" }
    # Reject redirections before either robocopy or its verification traverses a tree.
    foreach ($tree in @($source) + @($destinations | ForEach-Object { Join-Path $_ $entry.Directory })) {
        Assert-GameUiOrdinaryPath $tree
        if (Test-Path -LiteralPath $tree -PathType Container) {
            if (@(Get-ChildItem -LiteralPath $tree -Recurse -Force -Attributes ReparsePoint).Count -gt 0) { throw "Reparse points are not supported in asset trees: $tree" }
        }
    }
}
$leases = New-Object 'Collections.Generic.List[IDisposable]'
try {
    $manifest = $null
    if ($GameUiStyle -eq 'OfficialV1') {
        Assert-GameUiOrdinaryPath ([IO.Path]::GetFullPath($ReleaseManifestPath))
        $leases.Add([IO.File]::Open([IO.Path]::GetFullPath($ReleaseManifestPath), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read))
        $manifest = Get-Content -LiteralPath $ReleaseManifestPath -Raw | ConvertFrom-Json
        if ($manifest.schemaVersion -ne 1 -or $manifest.documentType -cne 'game-ui-release-manifest' -or $manifest.style -cne 'OfficialV1') { throw 'Invalid release manifest schema or style.' }
        if ($manifest.client.path -cne 'Zircon.exe' -or $manifest.package.path -cne 'Data\GameUI\OfficialV1.Zl') { throw 'Invalid release manifest file paths.' }
        if ((Get-GameUiContractHash $manifest.catalog) -cne $manifest.catalogContractSha256 -or
            (Get-GameUiBuildId $manifest) -cne $manifest.buildId) { throw 'Release manifest buildId or catalog contract mismatch.' }
        if ($manifest.catalog.runtimeReady -isnot [bool] -or !$manifest.catalog.runtimeReady) { throw 'Compiled OfficialV1 catalog is not runtime ready; deployment is blocked.' }
        $names = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($dependency in @($manifest.dependencies)) {
            if ($dependency.path -notmatch '^[^\\/:]+\.dll$|^Zircon\.exe\.config$' -or !$names.Add($dependency.path)) { throw 'Invalid or duplicate dependency path in release manifest.' }
        }
        $package = Join-Path $ResourceRoot $manifest.package.path
        $leases.Add([IO.File]::Open($package, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read))
        $packageRecord = Get-GameUiFileRecord $package $manifest.package.path
        if ($packageRecord.sha256 -cne $manifest.package.sha256 -or $packageRecord.length -ne $manifest.package.length -or
            $packageRecord.sha256 -cne $manifest.catalog.packageSha256) { throw 'Source UI package hash or length mismatch.' }
        # Preflight every target before copying any asset; a later bad target cannot leave earlier targets updated.
        foreach ($destination in $destinations) {
            $actualDependencies = @(Get-ChildItem -LiteralPath $destination -File -Force | Where-Object { $_.Extension -ieq '.dll' -or $_.Name -ieq 'Zircon.exe.config' })
            if ($actualDependencies.Count -ne $names.Count -or @($actualDependencies | Where-Object { !$names.Contains($_.Name) }).Count -gt 0) { throw 'Target dependency inventory does not match the release manifest.' }
            foreach ($record in @($manifest.client) + @($manifest.dependencies)) {
                $file = Join-Path $destination $record.path
                Assert-GameUiOrdinaryPath $file
                $leases.Add([IO.File]::Open($file, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read))
                $actual = Get-GameUiFileRecord $file $record.path
                if ($actual.sha256 -cne $record.sha256 -or $actual.length -ne $record.length) { throw "Client/dependency hash or length mismatch: $file" }
            }
            $contract = Invoke-GameUiContractInspection $destination $package
            if (!$contract.runtimeReady -or (Get-GameUiContractHash $contract) -cne $manifest.catalogContractSha256) { throw 'Target compiled catalog does not match the release manifest.' }
        }
    }
    foreach ($destination in $destinations) {
        foreach ($entry in $copyPlan) {
            $source = Join-Path $ResourceRoot $entry.Directory
            $target = Join-Path $destination $entry.Directory
            Write-Host "Preparing $target"
            # No /MIR, /PURGE or /MOVE: never remove destination files or alter source assets.
            & robocopy.exe $source $target @($entry.Filters) /E /COPY:DAT /DCOPY:DA /R:1 /W:1 /XJ /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "Asset copy failed: $source -> $target (robocopy $LASTEXITCODE)" }
            $files = @(Get-ChildItem -LiteralPath $source -File -Recurse | Where-Object {
                $fileName = $_.Name
                @($entry.Filters | Where-Object { $fileName -like $_ }).Count -gt 0
            })
            foreach ($file in $files) {
                $relative = $file.FullName.Substring($source.TrimEnd('\').Length + 1)
                $copied = Get-Item -LiteralPath (Join-Path $target $relative) -ErrorAction Stop
                if ($copied.Length -ne $file.Length) { throw "Asset size mismatch: $($copied.FullName)" }
            }
            Write-Host ("Verified {0} files in {1}" -f $files.Count, $target)
        }
        if ($GameUiStyle -eq 'OfficialV1') {
            $actualPackage = Get-GameUiFileRecord (Join-Path $destination $manifest.package.path) $manifest.package.path
            if ($actualPackage.sha256 -cne $manifest.package.sha256) { throw 'Deployed UI package SHA256 mismatch.' }
            Write-GameUiJsonAtomically (Join-Path $destination 'GameUiReleaseManifest.json') $manifest
            Write-Host ("Verified OfficialV1 build: {0}" -f $manifest.buildId)
        }
        Write-Host "Assets ready. Start Zircon.exe with working directory: $destination"
    }
} finally {
    foreach ($lease in $leases) { $lease.Dispose() }
}
# Robocopy returns 1 for successful copies; normalize success for callers.
$global:LASTEXITCODE = 0
