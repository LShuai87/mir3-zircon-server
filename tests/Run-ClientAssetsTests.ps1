# Isolated deployment regressions; no real client or user data is modified.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$setup = Join-Path (Split-Path -Parent $PSScriptRoot) 'build\Setup-ClientAssets.ps1'
$root = Join-Path ([System.IO.Path]::GetTempPath()) ('ZirconAssetTests-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'
$target = Join-Path $root 'output'
foreach ($folder in @('source\Data\Map Data', 'source\Map', 'source\Sound', 'source\Translations', 'output\Data')) {
    [System.IO.Directory]::CreateDirectory((Join-Path $root $folder)) | Out-Null
}
foreach ($file in @('Data\Interface1c.Zl','Data\Interface.Zl','Data\GameInter.Zl','Data\Pallete.png','Data\Map Data\Tiles.Zl','Map\0.map','Map\0.xob','Sound\Opening.wav')) {
    [System.IO.File]::WriteAllText((Join-Path $source $file), "fixture-$file")
}
foreach ($file in @('Data\System.db', 'Data\Users.db', 'Translations\EnglishMessages.ini')) {
    [System.IO.File]::WriteAllText((Join-Path $source $file), 'must-not-copy')
}
foreach ($file in @('Zircon.exe','Zircon.ini','Data\Users.db','Data\System.db')) {
    [System.IO.File]::WriteAllText((Join-Path $target $file), "preserve-$file")
}
$before = @{}
Get-ChildItem -LiteralPath $target -File -Recurse | ForEach-Object { $before[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName).Hash }
& $setup -ResourceRoot $source -OutputDirectories @($target)
& $setup -ResourceRoot $source -OutputDirectories @($target)
foreach ($file in $before.Keys) {
    if ((Get-FileHash -LiteralPath $file).Hash -ne $before[$file]) { throw "Existing client state was changed: $file" }
}
foreach ($relative in @('Data\Interface1c.Zl','Data\Map Data\Tiles.Zl','Map\0.map','Map\0.xob','Sound\Opening.wav')) {
    if ((Get-FileHash -LiteralPath (Join-Path $source $relative)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $target $relative)).Hash) {
        throw "Asset mismatch: $relative"
    }
}
if (Test-Path -LiteralPath (Join-Path $target 'Translations')) { throw 'Translations should not be copied' }
$rejected = $false
try { & $setup -ResourceRoot (Join-Path $root 'missing') -OutputDirectories @($target) } catch { $rejected = $true }
if (!$rejected) { throw 'Missing resource root should be rejected' }
$rejected = $false
try { & $setup -ResourceRoot $source -OutputDirectories @((Join-Path $root 'not-built')) } catch { $rejected = $true }
if (!$rejected) { throw 'Missing client executable should be rejected' }
Write-Host 'PASS: deployment, nested assets, idempotence, state preservation, exclusions and invalid inputs.'
Write-Host "Test directory: $root"