param([switch] $RunChecks, [switch] $PrepareUpdate)

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
$sources = @('DiskApp.cs', 'DiskData.cs', 'DiskFilesWindow.cs', 'ExplanationWindow.cs', 'FileComparison.cs',
    'FileComparisonWindow.cs', 'PathExplanation.cs',
    'PortableUpdate.cs', 'UiLanguage.cs', 'UpdateControls.cs') |
    ForEach-Object { Join-Path $source $_ }
$common = @('/nologo', '/codepage:65001', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Net.Http.dll')
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

if ($PrepareUpdate) {
    $version = [Reflection.AssemblyName]::GetAssemblyName($app).Version.ToString(3)
    $releaseFolder = Join-Path $artifacts 'release'
    $downloads = Join-Path $releaseFolder 'downloads'
    $updates = Join-Path $releaseFolder 'updates'
    [void](New-Item -Path $downloads,$updates -ItemType Directory -Force)
    $releaseApp = Join-Path $downloads "Disk Growth Tracker $version.exe"
    Copy-Item -LiteralPath $app -Destination $releaseApp -Force
    $manifest = Join-Path $updates 'latest.txt'
    "version=$version" | Set-Content -LiteralPath $manifest -Encoding ascii
    $directManifest = Join-Path $updates 'direct.txt'
    $hash = (Get-FileHash -LiteralPath $releaseApp -Algorithm SHA256).Hash.ToLowerInvariant()
    $size = (Get-Item -LiteralPath $releaseApp).Length
    @("version=$version", "sha256=$hash", "size=$size") | Set-Content -LiteralPath $directManifest -Encoding ascii
    Write-Output $releaseApp
    Write-Output $manifest
    Write-Output $directManifest
}
