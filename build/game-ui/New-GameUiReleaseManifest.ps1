# Generates a manifest from a compiled client and its actual staged UI package.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ClientDirectory,
    [Parameter(Mandatory = $true)][string]$ResourceRoot,
    [Parameter(Mandatory = $true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GameUiReleaseTools.ps1')
$clientRoot = Get-GameUiFullPath $ClientDirectory
$resourceDirectory = Get-GameUiFullPath $ResourceRoot
$output = [IO.Path]::GetFullPath($OutputPath)
foreach ($inputPath in @($clientRoot, $resourceDirectory, $output)) { Assert-GameUiOrdinaryPath $inputPath }
if (Test-Path -LiteralPath $output) { throw "Output already exists: $output" }
if ([IO.Path]::GetExtension($output) -ine '.json') { throw 'Manifest output must be a new .json file.' }
$packageRelative = 'Data\GameUI\OfficialV1.Zl'
$package = Join-Path $resourceDirectory $packageRelative
$client = Join-Path $clientRoot 'Zircon.exe'
$dependencies = @(Get-ChildItem -LiteralPath $clientRoot -File -Force | Where-Object { $_.Extension -ieq '.dll' -or $_.Name -ieq 'Zircon.exe.config' } | Sort-Object Name)
$leases = New-Object 'Collections.Generic.List[IDisposable]'
try {
    foreach ($file in @($client, $package) + @($dependencies | ForEach-Object { $_.FullName })) {
        Assert-GameUiOrdinaryPath $file
        $leases.Add([IO.File]::Open($file, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read))
    }
    $contract = Invoke-GameUiContractInspection $clientRoot $package
    $manifest = [ordered]@{ schemaVersion = 1; documentType = 'game-ui-release-manifest'; style = 'OfficialV1'; buildId = '';
        client = (Get-GameUiFileRecord $client 'Zircon.exe');
        package = (Get-GameUiFileRecord $package $packageRelative);
        dependencies = @($dependencies | ForEach-Object { Get-GameUiFileRecord $_.FullName $_.Name });
        catalogContractSha256 = (Get-GameUiContractHash $contract); catalog = $contract }
    if ($manifest.package.sha256 -cne $contract.packageSha256) { throw 'Compiled package SHA256 mismatch.' }
    $manifest.buildId = Get-GameUiBuildId $manifest
    Write-GameUiJsonAtomically $output $manifest -NoOverwrite
    Write-Host ("Manifest ready: {0}; RuntimeReady={1}" -f $manifest.buildId, $contract.runtimeReady)
    if (!$contract.runtimeReady) { Write-Host 'Inspection artifact only: OfficialV1 deployment remains blocked by the compiled readiness gate.' }
} finally {
    foreach ($lease in $leases) { $lease.Dispose() }
}
$global:LASTEXITCODE = 0
