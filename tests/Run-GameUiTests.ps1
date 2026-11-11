[CmdletBinding()]
param(
    [ValidateSet('Assets','Controls','Layout','Bindings','Npc')][string[]]$Suite = @('Assets'),
    [string]$PythonPath = 'python',
    [string]$CompilerPath,
    [string]$DecodedPath,
    [string]$ReferencePath,
    [string]$RenderPath,
    [string]$PackagePath,
    [string]$LegacyDataDirectory,
    [string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
foreach ($name in $Suite) {
    if ($name -notin @('Assets','Controls','Layout','Npc')) { throw "Suite $name is not implemented yet; it cannot be marked as passed." }
}
if ('Npc' -in $Suite) {
    if (!$PackagePath -or !$EvidenceDirectory) { throw 'Npc suite requires -PackagePath and a fresh -EvidenceDirectory.' }
    $npcArguments = @{ PackagePath = $PackagePath; EvidenceDirectory = $EvidenceDirectory }
    if ($CompilerPath) { $npcArguments.CompilerPath = $CompilerPath }
    if ($LegacyDataDirectory) { $npcArguments.LegacyDataDirectory = $LegacyDataDirectory }
    & (Join-Path $PSScriptRoot 'Run-GameUiNpcTests.ps1') @npcArguments
    if ($LASTEXITCODE -ne 0) { throw 'Official UI Npc tests failed.' }
    $Suite = @($Suite | Where-Object { $_ -ne 'Npc' })
    if ($Suite.Count -eq 0) { return }
}
if ('Assets' -in $Suite) {
& $PythonPath -m unittest discover -s (Join-Path $PSScriptRoot 'game-ui') -p test_decoder.py -v
if ($LASTEXITCODE -ne 0) { throw 'Official UI decoder tests failed.' }
}
if (!$CompilerPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $CompilerPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) { throw 'VS Roslyn compiler was not found.' }
$imageManagerDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '..\Debug\ImageManager'))
$imageManager = Join-Path $imageManagerDirectory 'ImageManager.exe'
if (!(Test-Path -LiteralPath $imageManager)) { throw 'Build ImageManager Debug before running Assets tests.' }
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ZirconGameUiTests-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
Copy-Item -LiteralPath $imageManager -Destination $testDirectory
Get-ChildItem -LiteralPath $imageManagerDirectory -Filter '*.dll' | Copy-Item -Destination $testDirectory
foreach ($name in $Suite) {
    $testClass = switch ($name) { Assets { 'OfficialUiAssetTests' } Controls { 'OfficialUiControlTests' } Layout { 'OfficialUiLayoutTests' } }
    $executable = Join-Path $testDirectory ($testClass + '.exe')
    $arguments = @('/nologo','/target:exe','/r:System.Drawing.dll','/r:System.Web.Extensions.dll',('/out:' + $executable),(Join-Path $PSScriptRoot ('game-ui\' + $testClass + '.cs')))
    if ($name -in @('Controls','Layout')) {
        $clientDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '..\Debug\Client'))
        $client = Join-Path $clientDirectory 'Zircon.exe'
        if (!(Test-Path -LiteralPath $client)) { throw 'Build Client Debug before running Controls tests.' }
        Copy-Item -LiteralPath $client -Destination $testDirectory
        Get-ChildItem -LiteralPath $clientDirectory -Filter '*.dll' | Copy-Item -Destination $testDirectory
        if ($name -eq 'Controls') { $arguments += @((Join-Path $PSScriptRoot 'game-ui\OfficialUiWindowTests.cs'), (Join-Path $PSScriptRoot 'game-ui\OfficialUiRenderTests.cs')) }
        $arguments += @((Join-Path $repositoryRoot 'ImageManager\Mir3Library.cs'),'/platform:x86',('/r:' + $client),('/r:' + (Join-Path $clientDirectory 'SlimDX.dll')),('/r:' + (Join-Path $clientDirectory 'Library.dll')),'/r:System.Windows.Forms.dll')
    } else { $arguments += @('/platform:x64',('/r:' + $imageManager)) }
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'ImageManager\app.config') -Destination ($executable + '.config')
    & $CompilerPath @arguments
    if ($LASTEXITCODE -ne 0) { throw "Official UI $name test compilation failed." }
    Push-Location -LiteralPath $testDirectory
    try {
        $runtimeArguments = @()
        if ($name -eq 'Controls') {
            if ($DecodedPath -or $ReferencePath) {
                if (!$DecodedPath -or !$ReferencePath) { throw 'Pass both -DecodedPath and -ReferencePath.' }
                $runtimeArguments += @($DecodedPath, $ReferencePath)
            }
            if ($RenderPath) { $runtimeArguments += @('--render', $RenderPath) }
        }
        & $executable @runtimeArguments
        if ($LASTEXITCODE -ne 0) { throw "Official UI $name tests failed." }
    } finally { Pop-Location }
}
