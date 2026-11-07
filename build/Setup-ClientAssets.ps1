# Deploys game assets only. Existing client settings and user databases are preserved.
[CmdletBinding()]
param(
    [string]$ResourceRoot,
    [string[]]$OutputDirectories
)
$ErrorActionPreference = 'Stop'
if (!$ResourceRoot) { $ResourceRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'data' }
$ResourceRoot = (Resolve-Path -LiteralPath $ResourceRoot).Path
if (!$OutputDirectories) {
    $workspaceParent = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $OutputDirectories = @((Join-Path $workspaceParent 'Debug\Client'))
}
$required = @('Data\Interface1c.Zl', 'Data\GameInter.Zl', 'Data\Interface.Zl', 'Data\Pallete.png', 'Sound\Opening.wav')
foreach ($relative in $required) {
    if (!(Test-Path -LiteralPath (Join-Path $ResourceRoot $relative) -PathType Leaf)) {
        throw "Required asset missing: $ResourceRoot\$relative"
    }
}
$copyPlan = @(
    @{ Directory = 'Data'; Filters = @('*.Zl', '*.png') },
    @{ Directory = 'Map'; Filters = @('*.map', '*.xob') },
    @{ Directory = 'Sound'; Filters = @('*.wav') }
)
foreach ($directory in $OutputDirectories) {
    $destination = [System.IO.Path]::GetFullPath($directory)
    if (!(Test-Path -LiteralPath (Join-Path $destination 'Zircon.exe') -PathType Leaf)) {
        throw "Build Client first; Zircon.exe is missing in $destination"
    }
    foreach ($entry in $copyPlan) {
        $source = Join-Path $ResourceRoot $entry.Directory
        $target = Join-Path $destination $entry.Directory
        if (!(Test-Path -LiteralPath $source -PathType Container)) { throw "Asset directory missing: $source" }
        if ([string]::Equals([System.IO.Path]::GetFullPath($source).TrimEnd('\'), [System.IO.Path]::GetFullPath($target).TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Source and destination must differ: $source"
        }
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
    Write-Host "Assets ready. Start Zircon.exe with working directory: $destination"
}
# Robocopy returns 1 for successful copies; normalize success for callers.
$global:LASTEXITCODE = 0