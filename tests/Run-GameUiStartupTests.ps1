# Exercises real Client controls in an isolated process; no network or game login.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PackagePath, [string]$CompilerPath, [string]$EvidenceDirectory, [string]$ClientDirectory)
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$ErrorActionPreference = 'Stop'
if ($EvidenceDirectory) {
    $EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
    if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Use a fresh evidence directory.' }
    [IO.Directory]::CreateDirectory($EvidenceDirectory) | Out-Null
}
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (!$CompilerPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $CompilerPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) { throw 'C# compiler not found.' }
if (!$ClientDirectory) { $ClientDirectory = Join-Path $repositoryRoot '..\Debug\Client' }
$clientDirectory = [IO.Path]::GetFullPath($ClientDirectory)
$client = Join-Path $clientDirectory 'Zircon.exe'
if (!(Test-Path -LiteralPath $client)) { throw 'Build the selected Client directory first.' }
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ZirconStartupTests-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
try {
    Copy-Item -LiteralPath $client -Destination $testDirectory
    Get-ChildItem -LiteralPath $clientDirectory -Filter '*.dll' | Copy-Item -Destination $testDirectory
    $executable = Join-Path $testDirectory 'OfficialUiStartupTests.exe'
    & $CompilerPath /nologo /target:exe /platform:x86 /r:System.Drawing.dll /r:System.Windows.Forms.dll ('/r:' + $client) ('/r:' + (Join-Path $clientDirectory 'Library.dll')) ('/r:' + (Join-Path $clientDirectory 'SlimDX.dll')) ('/out:' + $executable) (Join-Path $PSScriptRoot 'game-ui\OfficialUiStartupTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Startup test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'ImageManager\app.config') -Destination ($executable + '.config')
    Push-Location -LiteralPath $testDirectory
    try {
        & $executable $PackagePath
        if ($LASTEXITCODE -ne 0) { throw 'Startup tests failed.' }
        if ($EvidenceDirectory) {
            $runtime = foreach ($name in @('Zircon.exe', 'Library.dll', 'SlimDX.dll')) {
                $file = Join-Path $testDirectory $name
                [ordered]@{ name = $name; sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() }
            }
            $runtime | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'tested-runtime.json') -Encoding UTF8
        }
    }
    finally { Pop-Location }
} finally {
    $resolved = [IO.Path]::GetFullPath($testDirectory)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^ZirconStartupTests-[a-f0-9]{32}$') { throw 'Refusing cleanup outside generated startup test directory.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
