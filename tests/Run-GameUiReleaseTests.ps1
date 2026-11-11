# Isolated release/deployment integration. No real installation or game data is written.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [string]$ClientDirectory,
    [string]$CompilerPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path -Parent $PSScriptRoot
. (Join-Path $repository 'build\game-ui\GameUiReleaseTools.ps1')
if (!$ClientDirectory) { $ClientDirectory = Join-Path (Split-Path -Parent $repository) 'Debug\Client' }
if (!$CompilerPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $CompilerPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
}
if (!$CompilerPath) { throw 'C# compiler not found.' }
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
Assert-GameUiOrdinaryPath $evidence
if (Test-Path -LiteralPath $evidence) { throw 'Evidence directory must not exist.' }
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('ZirconUiReleaseTests-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$setup = Join-Path $repository 'build\Setup-ClientAssets.ps1'
$generate = Join-Path $repository 'build\game-ui\New-GameUiReleaseManifest.ps1'
$checks = New-Object 'Collections.Generic.List[object]'
function Assert-Check([string]$Name, [bool]$Condition) {
    if (!$Condition) { throw "FAIL: $Name" }
    $checks.Add([ordered]@{ name = $Name; result = 'pass' })
}
function Assert-Rejected([string]$Name, [scriptblock]$Action, [string]$MessagePattern) {
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    if ($null -ne $caught -and $caught.ToString() -notmatch $MessagePattern) { throw ("FAIL: {0}; expected error /{1}/; actual: {2}" -f $Name, $MessagePattern, $caught.ToString()) }
    Assert-Check $Name ($null -ne $caught -and $caught.ToString() -match $MessagePattern)
}
function Save-Manifest([string]$Path, $Manifest) {
    $Manifest.catalogContractSha256 = Get-GameUiContractHash $Manifest.catalog
    $Manifest.buildId = Get-GameUiBuildId $Manifest
    [IO.File]::WriteAllText($Path, ($Manifest | ConvertTo-Json -Depth 16), (New-Object Text.UTF8Encoding($false)))
}
function Get-TreeSnapshot([string]$Directory) {
    return (@(Get-ChildItem -LiteralPath $Directory -File -Recurse | Sort-Object FullName | ForEach-Object {
        $_.FullName.Substring($Directory.Length) + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash
    }) -join [char]10)
}
try {
    $source = Join-Path $testRoot 'assets'
    $fixture = Join-Path $testRoot 'compiled fixture'
    $target = Join-Path $testRoot 'deployed client'
    foreach ($directory in @('Data\GameUI', 'Data\Map Data', 'Map', 'Sound')) { [IO.Directory]::CreateDirectory((Join-Path $source $directory)) | Out-Null }
    foreach ($directory in @($fixture, $target)) { [IO.Directory]::CreateDirectory($directory) | Out-Null }
    foreach ($relative in @('Data\Interface1c.Zl', 'Data\Interface.Zl', 'Data\GameInter.Zl', 'Data\Pallete.png', 'Data\Map Data\Tiles.Zl', 'Map\0.map', 'Map\0.xob', 'Sound\Opening.wav')) {
        [IO.File]::WriteAllText((Join-Path $source $relative), "fixture-$relative")
    }
    $package = Join-Path $source 'Data\GameUI\OfficialV1.Zl'
    Copy-Item -LiteralPath $PackagePath -Destination $package
    $packageBytes = [IO.File]::ReadAllBytes($package)
    $actualManifest = Join-Path $testRoot 'actual-manifest.json'
    & $generate -ClientDirectory $ClientDirectory -ResourceRoot $source -OutputPath $actualManifest
    $actual = Get-Content -LiteralPath $actualManifest -Raw | ConvertFrom-Json
    Assert-Check 'actual compiled readiness remains false' ($actual.catalog.runtimeReady -eq $false)
    Assert-Check 'actual compiled frames and button groups are present' (@($actual.catalog.frames).Count -eq 91 -and @($actual.catalog.buttons).Count -eq 19)
    Assert-Check 'buildId survives JSON round trip' ($actual.buildId -ceq (Get-GameUiBuildId $actual))
    Assert-Check 'actual package hash matches catalog' ($actual.package.sha256 -ceq $actual.catalog.packageSha256)
    $repeatManifest = Join-Path $testRoot 'repeat-manifest.json'
    & $generate -ClientDirectory $ClientDirectory -ResourceRoot $source -OutputPath $repeatManifest
    Assert-Check 'manifest generation is byte deterministic' ((Get-FileHash $actualManifest).Hash -ceq (Get-FileHash $repeatManifest).Hash)
    Assert-Rejected 'existing manifest cannot be overwritten' { & $generate -ClientDirectory $ClientDirectory -ResourceRoot $source -OutputPath $actualManifest } 'Output already exists'
    Assert-Check 'existing manifest preserved' ((Get-FileHash $actualManifest).Hash -ceq (Get-FileHash $repeatManifest).Hash)

    $catalog = [IO.File]::ReadAllText((Join-Path $repository 'Client\Envir\GameUiOfficialV1Catalog.g.cs'))
    $marker = 'public const bool RuntimeReady = false;'
    Assert-Check 'fixture readiness substitution is unique' (($catalog.Split(@($marker), [StringSplitOptions]::None).Count - 1) -eq 1)
    $fixtureCatalog = Join-Path $testRoot 'FixtureCatalog.cs'
    [IO.File]::WriteAllText($fixtureCatalog, $catalog.Replace($marker, 'public const bool RuntimeReady = true;'))
    Copy-Item -LiteralPath (Join-Path $ClientDirectory 'SlimDX.dll') -Destination $fixture
    Copy-Item -LiteralPath (Join-Path $repository 'ImageManager\app.config') -Destination (Join-Path $fixture 'Zircon.exe.config')
    & $CompilerPath /nologo /target:library /platform:x86 /r:System.Drawing.dll ('/r:' + (Join-Path $fixture 'SlimDX.dll')) ('/out:' + (Join-Path $fixture 'Zircon.exe')) $fixtureCatalog (Join-Path $repository 'Client\Envir\GameUiPackagePreflight.cs') (Join-Path $PSScriptRoot 'game-ui\GameUiReleaseFixture.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Release fixture compilation failed.' }
    Copy-Item -Path (Join-Path $fixture '*') -Destination $target
    foreach ($relative in @('Zircon.ini', 'Data\Users.db', 'Data\System.db')) {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName((Join-Path $target $relative))) | Out-Null
        [IO.File]::WriteAllText((Join-Path $target $relative), "preserve-$relative")
    }
    $preserved = @('Zircon.ini', 'Data\Users.db', 'Data\System.db', 'Zircon.exe', 'SlimDX.dll', 'Zircon.exe.config')
    $preservedHashes = @{}
    foreach ($relative in $preserved) { $preservedHashes[$relative] = (Get-FileHash (Join-Path $target $relative)).Hash }
    $officialManifest = Join-Path $testRoot 'official-fixture-manifest.json'
    & $generate -ClientDirectory $fixture -ResourceRoot $source -OutputPath $officialManifest
    $ready = Get-Content -LiteralPath $officialManifest -Raw | ConvertFrom-Json
    Assert-Check 'fixture alone has compiled readiness true' ($ready.catalog.runtimeReady -eq $true)
    Assert-Check 'same resource contract frames in real and fixture clients' (($actual.catalog.frames | ConvertTo-Json -Depth 6 -Compress) -ceq ($ready.catalog.frames | ConvertTo-Json -Depth 6 -Compress))
    $initial = Get-TreeSnapshot $target
    Assert-Rejected 'Official requires explicit manifest' { & $setup -ResourceRoot $source -OutputDirectories @($target) -GameUiStyle OfficialV1 } 'requires -ReleaseManifestPath'
    Assert-Rejected 'unready actual client manifest cannot deploy' { & $setup -ResourceRoot $source -OutputDirectories @($target) -GameUiStyle OfficialV1 -ReleaseManifestPath $actualManifest } 'not runtime ready'
    Assert-Check 'rejected preflight changed no target files' ((Get-TreeSnapshot $target) -ceq $initial)
    $sourceBefore = Get-TreeSnapshot $source
    & $setup -ResourceRoot $source -OutputDirectories @($target) -GameUiStyle OfficialV1 -ReleaseManifestPath $officialManifest
    & $setup -ResourceRoot $source -OutputDirectories @($target) -GameUiStyle OfficialV1 -ReleaseManifestPath $officialManifest
    Assert-Check 'Official matching build deployed' ((Get-FileHash (Join-Path $target $ready.package.path)).Hash.ToLowerInvariant() -ceq $ready.package.sha256)
    Assert-Check 'deployed manifest is same build' ((Get-Content (Join-Path $target 'GameUiReleaseManifest.json') -Raw | ConvertFrom-Json).buildId -ceq $ready.buildId)
    foreach ($relative in $preserved) { Assert-Check ("preserved $relative") ((Get-FileHash (Join-Path $target $relative)).Hash -ceq $preservedHashes[$relative]) }
    Assert-Check 'nested Zl deployed with exact hash' ((Get-FileHash (Join-Path $target 'Data\Map Data\Tiles.Zl')).Hash -ceq (Get-FileHash (Join-Path $source 'Data\Map Data\Tiles.Zl')).Hash)
    Assert-Check 'deployment did not modify source' ((Get-TreeSnapshot $source) -ceq $sourceBefore)
    $deployed = Get-TreeSnapshot $target

    $badManifest = Join-Path $testRoot 'bad-manifest.json'
    $changed = Get-Content -LiteralPath $officialManifest -Raw | ConvertFrom-Json
    $changed.buildId = 'wrong-build'
    [IO.File]::WriteAllText($badManifest, ($changed | ConvertTo-Json -Depth 16))
    Assert-Rejected 'bad buildId rejected' { & $setup -ResourceRoot $source -OutputDirectories @($target) -GameUiStyle OfficialV1 -ReleaseManifestPath $badManifest } 'buildId or catalog'
    $changed = Get-Content -LiteralPath $officialManifest -Raw | ConvertFrom-Json
    $changed.catalog.frames[0].sha256 = '0' * 64
    Save-Manifest $badManifest $changed
    Assert-Rejected 'forged resource contract rejected against compiled client' { & $setup -ResourceRoot $source -OutputDirectories @($target) -GameUiStyle OfficialV1 -ReleaseManifestPath $badManifest } 'Target compiled catalog'
    $changed = Get-Content -LiteralPath $officialManifest -Raw | ConvertFrom-Json
    $changed.dependencies[0].path = '..\SlimDX.dll'
    Save-Manifest $badManifest $changed
    Assert-Rejected 'dependency traversal rejected' { & $setup -ResourceRoot $source -OutputDirectories @($target) -GameUiStyle OfficialV1 -ReleaseManifestPath $badManifest } 'Invalid or duplicate dependency'
    $changed = Get-Content -LiteralPath $officialManifest -Raw | ConvertFrom-Json
    $changed.dependencies = @($changed.dependencies) + @($changed.dependencies[0])
    Save-Manifest $badManifest $changed
    Assert-Rejected 'duplicate dependency rejected' { & $setup -ResourceRoot $source -OutputDirectories @($target) -GameUiStyle OfficialV1 -ReleaseManifestPath $badManifest } 'Invalid or duplicate dependency'
    $changed = Get-Content -LiteralPath $officialManifest -Raw | ConvertFrom-Json
    $changed.dependencies = @($changed.dependencies | Where-Object { $_.path -ine 'SlimDX.dll' })
    Save-Manifest $badManifest $changed
    Assert-Rejected 'omitted dependency rejected' { & $setup -ResourceRoot $source -OutputDirectories @($target) -GameUiStyle OfficialV1 -ReleaseManifestPath $badManifest } 'dependency inventory'
    [IO.File]::WriteAllBytes($package, $packageBytes[0..($packageBytes.Length - 2)])
    Assert-Rejected 'truncated source package rejected before copy' { & $setup -ResourceRoot $source -OutputDirectories @($target) -GameUiStyle OfficialV1 -ReleaseManifestPath $officialManifest } 'Source UI package'
    Assert-Rejected 'manifest generation rejects actual corrupt package' { & $generate -ClientDirectory $fixture -ResourceRoot $source -OutputPath (Join-Path $testRoot 'corrupt.json') } 'inspection failed'
    Assert-Check 'failed generation publishes no manifest' (!(Test-Path -LiteralPath (Join-Path $testRoot 'corrupt.json')))
    [IO.File]::WriteAllBytes($package, $packageBytes)
    $changedBytes = [byte[]]$packageBytes.Clone()
    $changedBytes[-1] = $changedBytes[-1] -bxor 1
    [IO.File]::WriteAllBytes($package, $changedBytes)
    Assert-Rejected 'same-length corrupt source package rejected' { & $setup -ResourceRoot $source -OutputDirectories @($target) -GameUiStyle OfficialV1 -ReleaseManifestPath $officialManifest } 'Source UI package'
    [IO.File]::WriteAllBytes($package, $packageBytes)
    $hiddenPackage = Join-Path $testRoot 'saved-package.Zl'
    Move-Item -LiteralPath $package -Destination $hiddenPackage
    Assert-Rejected 'missing package rejected before copy' { & $setup -ResourceRoot $source -OutputDirectories @($target) -GameUiStyle OfficialV1 -ReleaseManifestPath $officialManifest } 'Required asset missing'
    Move-Item -LiteralPath $hiddenPackage -Destination $package
    foreach ($relative in @('Zircon.exe', 'SlimDX.dll')) {
        $file = Join-Path $target $relative
        $bytes = [IO.File]::ReadAllBytes($file)
        $changedBytes = [byte[]]$bytes.Clone()
        $changedBytes[-1] = $changedBytes[-1] -bxor 1
        [IO.File]::WriteAllBytes($file, $changedBytes)
        Assert-Rejected ("changed $relative rejected") { & $setup -ResourceRoot $source -OutputDirectories @($target) -GameUiStyle OfficialV1 -ReleaseManifestPath $officialManifest } 'Client/dependency hash'
        [IO.File]::WriteAllBytes($file, $bytes)
    }
    Assert-Check 'negative deployment checks preserved existing output' ((Get-TreeSnapshot $target) -ceq $deployed)
    $other = Join-Path $testRoot 'second target'
    [IO.Directory]::CreateDirectory($other) | Out-Null
    [IO.File]::WriteAllText((Join-Path $other 'Zircon.exe'), 'bad-client')
    [IO.File]::WriteAllText((Join-Path $source 'Data\Map Data\Tiles.Zl'), 'new-tile-bytes')
    Assert-Rejected 'all targets checked before first target copy' { & $setup -ResourceRoot $source -OutputDirectories @($target, $other) -GameUiStyle OfficialV1 -ReleaseManifestPath $officialManifest } 'Client/dependency hash|dependency inventory'
    Assert-Check 'first target unchanged when second target invalid' ((Get-TreeSnapshot $target) -ceq $deployed)
    Assert-Rejected 'source cannot contain destination' { & $setup -ResourceRoot $source -OutputDirectories @($source) } 'must be disjoint'
    Assert-Rejected 'destination cannot contain source' { & $setup -ResourceRoot $source -OutputDirectories @($testRoot) } 'must be disjoint'
    $realTarget = Join-Path $testRoot 'real client target'
    [IO.Directory]::CreateDirectory($realTarget) | Out-Null
    Copy-Item -LiteralPath (Join-Path $ClientDirectory 'Zircon.exe') -Destination $realTarget
    Get-ChildItem -LiteralPath $ClientDirectory -File | Where-Object { $_.Extension -ieq '.dll' -or $_.Name -ieq 'Zircon.exe.config' } | Copy-Item -Destination $realTarget
    $forgedReady = Get-Content -LiteralPath $actualManifest -Raw | ConvertFrom-Json
    $forgedReady.catalog.runtimeReady = $true
    Save-Manifest $badManifest $forgedReady
    Assert-Rejected 'manifest cannot override real compiled readiness false' { & $setup -ResourceRoot $source -OutputDirectories @($realTarget) -GameUiStyle OfficialV1 -ReleaseManifestPath $badManifest } 'Target compiled catalog'

    Copy-Item -LiteralPath $actualManifest -Destination (Join-Path $evidence 'actual-client-manifest.json')
    Copy-Item -LiteralPath $officialManifest -Destination (Join-Path $evidence 'fixture-manifest.json')
    Copy-Item -LiteralPath (Join-Path $target 'GameUiReleaseManifest.json') -Destination (Join-Path $evidence 'fixture-deployed-manifest.json')
    $result = [ordered]@{ checks = $checks.Count; results = @($checks.ToArray()); actualRuntimeReady = $actual.catalog.runtimeReady;
        actualFrameCount = @($actual.catalog.frames).Count; actualButtonCount = @($actual.catalog.buttons).Count;
        officialSuccessUsesTestOnlyCompiledReadyFixture = $true; gameAcceptanceExecuted = $false }
    [IO.File]::WriteAllText((Join-Path $evidence 'results.json'), ($result | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
    Write-Host ("PASS: {0} release/deployment checks; real catalog remains blocked; Official success is a test-only fixture." -f $checks.Count)
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^ZirconUiReleaseTests-[a-f0-9]{32}$') { throw 'Refusing cleanup outside generated release test directory.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
$global:LASTEXITCODE = 0
