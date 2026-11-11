# Exercises real Client controls in an isolated process; no network or game login.
[CmdletBinding()]
param([string]$CompilerPath, [string]$RenderPath, [string]$PackagePath)
$ErrorActionPreference = 'Stop'
if ($RenderPath) {
    if ($PackagePath) { $PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path }
    $RenderPath = [IO.Path]::GetFullPath($RenderPath)
    if (Test-Path -LiteralPath $RenderPath) { throw 'Use a fresh render evidence directory.' }
}
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (!$CompilerPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $CompilerPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) { throw 'C# compiler not found.' }
$clientDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '..\Debug\Client'))
$client = Join-Path $clientDirectory 'Zircon.exe'
if (!(Test-Path -LiteralPath $client)) { throw 'Build Client Debug first.' }
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ZirconMiniMapLayoutTests-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
try {
    $layoutInput = Get-Content -LiteralPath (Join-Path $repositoryRoot 'build/game-ui/layout-official-v1.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $byId = @{}
    foreach ($node in $layoutInput.nodes) { $byId[$node.id] = $node }
    $owner = $layoutInput.owners.minimap
    $expected = @(($owner.size -join ','))
    foreach ($id in @('minimap.viewport','minimap.coordinates','minimap.collapse','minimap.safety','minimap.title','minimap.footer.bigMap','minimap.footer.transparency','minimap.footer.zoom')) { $expected += ($byId[$id].rect -join ',') }
    $fixture = Join-Path $testDirectory 'expected-layout.txt'
    [IO.File]::WriteAllLines($fixture,$expected,[Text.Encoding]::UTF8)
    Copy-Item -LiteralPath $client -Destination $testDirectory
    Get-ChildItem -LiteralPath $clientDirectory -Filter '*.dll' | Copy-Item -Destination $testDirectory
    $executable = Join-Path $testDirectory 'OfficialUiMiniMapLayoutTests.exe'
    & $CompilerPath /nologo /target:exe /platform:x86 /r:System.Drawing.dll /r:System.Windows.Forms.dll ('/r:' + $client) ('/r:' + (Join-Path $clientDirectory 'Library.dll')) ('/r:' + (Join-Path $clientDirectory 'SlimDX.dll')) ('/out:' + $executable) (Join-Path $PSScriptRoot 'game-ui\OfficialUiMiniMapLayoutTests.cs') (Join-Path $PSScriptRoot 'game-ui\OfficialUiMiniMapRenderTests.cs') (Join-Path $PSScriptRoot 'game-ui\OfficialUiMiniMapToolbarTests.cs') (Join-Path $PSScriptRoot 'game-ui\OfficialUiMiniMapViewportTests.cs') (Join-Path $repositoryRoot 'ImageManager\Mir3Library.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Minimap layout test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'ImageManager\app.config') -Destination ($executable + '.config')
    if ($RenderPath) {
        [IO.Directory]::CreateDirectory($RenderPath) | Out-Null
        $runtime = foreach ($name in @('Zircon.exe', 'Library.dll', 'SlimDX.dll')) {
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes((Join-Path $testDirectory $name)))).Replace('-', '').ToLowerInvariant() }
            finally { $sha.Dispose() }
            [ordered]@{ name = $name; sha256 = $hash }
        }
        if ($PackagePath) {
            [IO.File]::WriteAllText((Join-Path $RenderPath 'tested-package.json'), (ConvertTo-Json -InputObject ([ordered]@{ path = $PackagePath; sha256 = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant() })), [Text.UTF8Encoding]::new($false))
        }
        [IO.File]::WriteAllText((Join-Path $RenderPath 'tested-runtime.json'), (ConvertTo-Json -InputObject @($runtime) -Depth 4), [Text.UTF8Encoding]::new($false))
    }
    Push-Location -LiteralPath $testDirectory
    try { if ($RenderPath) { & $executable $fixture $RenderPath $PackagePath } else { & $executable $fixture }; if ($LASTEXITCODE -ne 0) { throw 'Minimap layout tests failed.' } }
    finally { Pop-Location }
} finally {
    $resolved = [IO.Path]::GetFullPath($testDirectory)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^ZirconMiniMapLayoutTests-[a-f0-9]{32}$') { throw 'Refusing cleanup outside generated minimap layout test directory.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
