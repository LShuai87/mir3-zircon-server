$ErrorActionPreference = 'Stop'
$scriptPath = 'E:\workspace\mir3-zircon-server\build\game-ui\Build-OfficialUi.ps1'
$officialData = 'E:\Program Files\ShengquGames\Mir3\Data'
$workRoot = 'E:\workspace\mir3-ui-work'
$published = Join-Path $workRoot 'reference-build-v1'
$cases = @()
function Expect-Failure([string]$Name, [hashtable]$Arguments, [string]$Expected) {
    $caught = $null
    try { & $scriptPath @Arguments } catch { $caught = $_.Exception.Message }
    if (!$caught -or $caught -notlike ('*' + $Expected + '*')) { throw "Unexpected guard result for ${Name}: $caught" }
    Write-Host "PASS ${Name}: $caught"
    return [ordered]@{ name=$Name; result='pass'; message=$caught }
}
$existingHash = (Get-FileHash -LiteralPath (Join-Path $published 'build.json') -Algorithm SHA256).Hash
$cases += Expect-Failure 'existing-output' @{SourcePath=$officialData; OutputPath=$published} 'Output must be a new directory'
$cases += Expect-Failure 'runtime-not-ready' @{Mode='Runtime'; SourcePath=$officialData; OutputPath=(Join-Path $workRoot 'runtime-must-not-exist')} 'Runtime packaging is unavailable'
$cases += Expect-Failure 'source-overlap' @{SourcePath=$officialData; OutputPath=$officialData} 'must be disjoint'
$failureOutput = Join-Path $workRoot ('guard-failure-' + [guid]::NewGuid().ToString('N'))
$stagingPattern = '.' + [IO.Path]::GetFileName($failureOutput) + '.staging-*'
$pythonExe = (& python -c 'import sys; print(sys.executable)').Trim()
$cases += Expect-Failure 'builder-process-failure' @{SourcePath=$officialData; OutputPath=$failureOutput; ImageManagerPath=$pythonExe} 'Reference Zl conversion failed'
if ((Test-Path -LiteralPath $failureOutput) -or @(Get-ChildItem -LiteralPath $workRoot -Directory -Force -Filter $stagingPattern).Count -ne 0) { throw 'Failed conversion leaked published output or staging.' }
if ((Get-FileHash -LiteralPath (Join-Path $published 'build.json') -Algorithm SHA256).Hash -ne $existingHash) { throw 'Existing output changed.' }
[ordered]@{ schemaVersion=1; cases=$cases; failedOutputAbsent=$true; failureStagingRemoved=$true; previousBuildMetadataUnchanged=$true } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $workRoot 'acceptance\build-guards.json') -Encoding utf8
