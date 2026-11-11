# File staging and rejection checks in a fresh disposable client; no game process or screenshots.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PackagePath, [Parameter(Mandatory = $true)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$prepare = Join-Path $repositoryRoot 'build\game-ui\Prepare-GameUiPreview.ps1'
$runtime = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '..\Debug\Client'))
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Use a fresh evidence directory.' }
[IO.Directory]::CreateDirectory($EvidenceDirectory) | Out-Null
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('ZirconUiPreview-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$script:previewChecks = 0
function Check([bool]$value, [string]$message) {
    if (!$value) { throw $message }; $script:previewChecks++
}
function Reject([scriptblock]$action, [string]$reason) {
    $caught = $null
    try { & $action | Out-Null } catch { $caught = $_ }
    Check ($null -ne $caught -and $caught.ToString().Contains($reason)) ("Missing expected rejection: $reason; actual: $caught")
}
try {
    $client = Join-Path $testRoot 'client with spaces & symbols'
    [IO.Directory]::CreateDirectory($client) | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $runtime -File | Where-Object { $_.Extension -ieq '.dll' -or $_.Name -in @('Zircon.exe', 'Zircon.exe.config') }) {
        Copy-Item -LiteralPath $file.FullName -Destination $client
    }
    $ini = Join-Path $client 'Zircon.ini'
    [IO.File]::WriteAllText($ini, "[Graphics]`r`nGameUiStyle=Legacy`r`nGameSize=1024, 768`r`n")
    $before = (Get-FileHash -LiteralPath $ini).Hash
    $target = Join-Path $client 'Data\GameUI\OfficialV1.Zl'
    $preview = Join-Path $client 'Start-OfficialUiPreview.cmd'
    $legacy = Join-Path $client 'Start-LegacyUi.cmd'
    $valid = & $prepare -ClientDirectory $client -PackagePath $PackagePath -ValidateOnly
    Check ($valid.validatedOnly -and $valid.frameCount -eq 94 -and !$valid.runtimeReady) 'Validation changed compiled gate or package contract.'
    Check (!(Test-Path -LiteralPath $target) -and !(Test-Path -LiteralPath $preview)) 'Validation-only staged files.'
    $corrupt = Join-Path $testRoot 'corrupt.Zl'
    [IO.File]::WriteAllText($corrupt, 'invalid candidate')
    Reject { & $prepare -ClientDirectory $client -PackagePath $corrupt } 'inspection failed'
    Check (!(Test-Path -LiteralPath $target) -and !(Test-Path -LiteralPath $preview)) 'Invalid source mutated target.'
    [IO.File]::WriteAllText($preview, 'preserved user launcher')
    Reject { & $prepare -ClientDirectory $client -PackagePath $PackagePath } 'different launcher'
    Check ([IO.File]::ReadAllText($preview) -ceq 'preserved user launcher' -and !(Test-Path -LiteralPath $target)) 'Conflicting launcher was overwritten or package was staged.'
    Remove-Item -LiteralPath $preview
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
    [IO.File]::WriteAllText($target, 'preserved different package')
    Reject { & $prepare -ClientDirectory $client -PackagePath $PackagePath } 'different package'
    Check ([IO.File]::ReadAllText($target) -ceq 'preserved different package' -and !(Test-Path -LiteralPath $preview)) 'Conflicting package was overwritten or launcher was staged.'
    Remove-Item -LiteralPath $target
    $first = & $prepare -ClientDirectory $client -PackagePath $PackagePath
    Check (!$first.validatedOnly -and (Test-Path -LiteralPath $target) -and (Test-Path -LiteralPath $preview) -and (Test-Path -LiteralPath $legacy)) 'Valid preview was not staged.'
    Check ((Get-FileHash -LiteralPath $target).Hash -ceq (Get-FileHash -LiteralPath $PackagePath).Hash) 'Staged package differs.'
    Check ([IO.File]::ReadAllText($preview).Contains('start "" "%~dp0Zircon.exe" --game-ui-preview')) 'Preview launcher command changed.'
    Check ([IO.File]::ReadAllText($legacy).Contains('start "" "%~dp0Zircon.exe" --game-ui-legacy')) 'Legacy launcher command changed.'
    $second = & $prepare -ClientDirectory $client -PackagePath $target
    Check ($second.packageSha256 -ceq $first.packageSha256) 'Idempotent staging / source=destination failed.'
    Check ((Get-FileHash -LiteralPath $ini).Hash -ceq $before) 'Staging modified INI / resolution.'
    Reject { & $prepare -ClientDirectory 'E:\Program Files\ShengquGames\Mir3' -PackagePath $PackagePath } 'read-only'
    $first | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'staging-result.json') -Encoding UTF8
    $records = foreach ($name in @('Zircon.exe', 'Library.dll', 'SlimDX.dll')) {
        [ordered]@{ name = $name; sha256 = (Get-FileHash -LiteralPath (Join-Path $client $name)).Hash.ToLowerInvariant() }
    }
    $records | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'tested-runtime.json') -Encoding UTF8
    Write-Output ("Manual-preview staging: $script:previewChecks checks passed; no launch, INI edits, screenshots or release-gate changes.")
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^ZirconUiPreview-[a-f0-9]{32}$') { throw 'Refusing cleanup outside generated preview test directory.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
