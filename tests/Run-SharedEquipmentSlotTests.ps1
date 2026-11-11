[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ServerDirectory, [string]$CompilerPath)
$ErrorActionPreference='Stop'
$ServerDirectory=(Resolve-Path -LiteralPath $ServerDirectory).Path
if (!$CompilerPath) {
    $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $CompilerPath=& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
}
$testDirectory=Join-Path ([IO.Path]::GetTempPath()) ('ZirconSharedEquipmentTests-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
try {
    Get-ChildItem -LiteralPath $ServerDirectory -File | Where-Object {$_.Extension -in @('.exe','.dll','.config')} | Copy-Item -Destination $testDirectory
    $exe=Join-Path $testDirectory 'SharedEquipmentSlotTests.exe'
    & $CompilerPath /nologo /target:exe /platform:x64 /r:System.Drawing.dll ('/r:'+(Join-Path $ServerDirectory 'Server.exe')) ('/r:'+(Join-Path $ServerDirectory 'Library.dll')) ('/out:'+$exe) (Join-Path $PSScriptRoot 'SharedEquipmentSlotTests.cs')
    if ($LASTEXITCODE -ne 0) {throw 'Shared equipment compilation failed'}
    Copy-Item (Join-Path $ServerDirectory 'Server.exe.config') ($exe+'.config')
    Push-Location -LiteralPath $testDirectory
    try {& $exe; if ($LASTEXITCODE -ne 0) {throw 'Shared equipment tests failed'}} finally {Pop-Location}
} finally {
    $resolved=[IO.Path]::GetFullPath($testDirectory)
    $prefix=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if (!$resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^ZirconSharedEquipmentTests-[a-f0-9]{32}$') {throw 'Unsafe test cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}