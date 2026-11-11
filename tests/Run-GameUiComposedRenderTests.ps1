[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PackagePath,
    [Parameter(Mandatory=$true)][string]$VerificationPath,
    [Parameter(Mandatory=$true)][string]$RenderPath,
    [string]$CompilerPath
)
$ErrorActionPreference='Stop'
$repositoryRoot=Split-Path -Parent $PSScriptRoot
if (!$CompilerPath) {
    $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $CompilerPath=& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) { throw 'VS Roslyn compiler not found' }
$testDirectory=Join-Path ([IO.Path]::GetTempPath()) ('ZirconComposedUi-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
$clientDirectory=[IO.Path]::GetFullPath((Join-Path $repositoryRoot '../Debug/Client'))
Get-ChildItem -LiteralPath $clientDirectory -Filter '*.dll' | Copy-Item -Destination $testDirectory
Copy-Item -LiteralPath (Join-Path $clientDirectory 'Zircon.exe') -Destination $testDirectory
$executable=Join-Path $testDirectory 'OfficialUiComposedRenderTests.exe'
$arguments=@('/nologo','/target:exe','/platform:x86','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Windows.Forms.dll',
    ('/r:'+ (Join-Path $testDirectory 'Zircon.exe')),
    ('/r:'+ (Join-Path $testDirectory 'SlimDX.dll')),('/r:'+ (Join-Path $testDirectory 'Library.dll')),
    ('/out:'+ $executable),(Join-Path $PSScriptRoot 'game-ui/OfficialUiComposedRenderTests.cs'))
& $CompilerPath @arguments
if ($LASTEXITCODE -ne 0) { throw 'Composed render test compilation failed' }
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'ImageManager/app.config') -Destination ($executable+'.config')
Push-Location -LiteralPath $testDirectory
try {
    & $executable $PackagePath $VerificationPath $RenderPath
    if ($LASTEXITCODE -ne 0) { throw 'Composed render tests failed' }
} finally { Pop-Location }
