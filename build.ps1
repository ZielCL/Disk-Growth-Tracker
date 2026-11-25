param([switch] $RunChecks)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler -PathType Leaf)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (!(Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The Windows .NET Framework C# compiler was not found.'
}

$source = Join-Path $PSScriptRoot 'src'
$artifacts = Join-Path $PSScriptRoot 'artifacts'
[void](New-Item -Path $artifacts -ItemType Directory -Force)
$app = Join-Path $artifacts 'Disk Growth Tracker.exe'
$sources = @('DiskApp.cs', 'DiskData.cs', 'DiskFilesWindow.cs', 'UiLanguage.cs') |
    ForEach-Object { Join-Path $source $_ }
$common = @('/nologo', '/codepage:65001', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll')
$resources = @(
    ("/win32manifest:" + (Join-Path $source 'App.manifest'))
    ("/win32icon:" + (Join-Path $source 'App.ico'))
)

& $compiler @common /target:winexe /platform:anycpu /optimize+ "/out:$app" @resources @sources
if ($LASTEXITCODE -ne 0) { throw "Compilation failed (exit $LASTEXITCODE)." }
Write-Output $app

if ($RunChecks) {
    $runner = Join-Path $artifacts 'DiskGrowth.Checks.exe'
    $checks = Join-Path $PSScriptRoot 'tests\Checks.cs'
    $fixtures = Join-Path $artifacts ("qa-" + [Guid]::NewGuid().ToString('N').Substring(0, 10))
    & $compiler @common /target:exe /main:DiskGrowth.Checks "/out:$runner" $resources[0] @sources $checks
    if ($LASTEXITCODE -ne 0) { throw "Check compilation failed (exit $LASTEXITCODE)." }
    & $runner $fixtures
    if ($LASTEXITCODE -ne 0) { throw "Checks failed (exit $LASTEXITCODE)." }
}
