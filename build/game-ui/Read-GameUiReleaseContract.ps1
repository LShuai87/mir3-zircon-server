# Isolated x86 build/deployment inspection. Never starts Program or creates a GPU device.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ClientDirectory,
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::Is64BitProcess -or $PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Run this inspector in x86 Windows PowerShell (.NET Framework).'
}
$client = Join-Path $ClientDirectory 'Zircon.exe'
$assembly = [Reflection.Assembly]::LoadFrom($client)
$catalogType = $assembly.GetType('Client.Envir.GameUiOfficialV1Catalog', $true)
$runtimeReady = $catalogType.GetField('RuntimeReady').GetRawConstantValue()
$relative = $catalogType.GetField('PackageRelativePath').GetRawConstantValue()
$packageHash = $catalogType.GetField('PackageSha256').GetRawConstantValue()
$sourceHash = $catalogType.GetField('ManifestSha256').GetRawConstantValue()
if ($relative -cne 'Data\GameUI\OfficialV1.Zl') { throw 'Unexpected compiled package path.' }
$manifest = $catalogType.GetProperty('PackageManifest').GetValue($null, $null)
$verifiedType = $assembly.GetType('Client.Envir.GameUiVerifiedPackage', $true)
$verified = $null
try {
    # Uses the exact client's full Zl/header/frame/SHA256 validator; no UploadAllFrames.
    $verified = $verifiedType.GetMethod('Verify').Invoke($null, @($PackagePath, $manifest))
    $frames = @($manifest.Frames.Values | Sort-Object Index | ForEach-Object {
        [ordered]@{ id = $_.Id; index = $_.Index; width = $_.Size.Width; height = $_.Size.Height;
            offsetX = $_.Offset.X; offsetY = $_.Offset.Y; sha256 = $_.DataSha256 }
    })
    $buttonMap = $catalogType.GetProperty('Buttons').GetValue($null, $null)
    $buttons = @($buttonMap.Keys | Sort-Object | ForEach-Object {
        $button = $buttonMap[$_]
        [ordered]@{ id = $_; normal = $button.Normal; hover = $button.Hover;
            pressed = $button.Pressed; disabled = $button.Disabled;
            normalTint = $button.NormalTint.ToArgb(); hoverTint = $button.HoverTint.ToArgb();
            pressedTint = $button.PressedTint.ToArgb(); disabledTint = $button.DisabledTint.ToArgb() }
    })
    $contract = [ordered]@{ runtimeReady = [bool]$runtimeReady; packageRelativePath = $relative;
        packageSha256 = $packageHash; sourceManifestSha256 = $sourceHash; frames = $frames; buttons = $buttons }
    $json = $contract | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $json + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
} finally {
    if ($null -ne $verified) { $verified.Dispose() }
}
