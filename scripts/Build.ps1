param([string]$Version = '', [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $repoRoot 'FlareLabels.csproj'
$project = [xml](Get-Content -LiteralPath $projectPath -Raw)
$projectVersion = [string]$project.Project.PropertyGroup.Version
if (!$Version) {$Version = $projectVersion}
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or $Version -ne $projectVersion) {throw 'Release version must match the project version.'}
$releaseRoot = Join-Path $repoRoot "dist\v$Version"
$appDirectory = Join-Path $releaseRoot 'app'
[IO.Directory]::CreateDirectory($appDirectory) | Out-Null
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$dotnetExecutable = if ($env:DOTNET_ROOT) {Join-Path $env:DOTNET_ROOT 'dotnet.exe'} else {(Get-Command dotnet).Source}
& $dotnetExecutable restore $projectPath -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) {throw 'Restore failed.'}
& $dotnetExecutable build $projectPath -c Release --no-restore
if ($LASTEXITCODE -ne 0) {throw 'Build failed.'}
$builtDll = Join-Path $repoRoot 'bin\Release\net10.0-windows\win-x64\FlareLabels.dll'
if (!$SkipTests) {
    $testDirectory = Join-Path $releaseRoot ('tests-' + [guid]::NewGuid().ToString('N'))
    & $dotnetExecutable $builtDll --self-test (Join-Path $repoRoot 'tests\Contacts.csv') $testDirectory
    if ($LASTEXITCODE -ne 0) {throw 'App validation failed. See self-test-error.txt.'}
}
& $dotnetExecutable publish $projectPath -c Release --no-restore -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o $appDirectory
if ($LASTEXITCODE -ne 0) {throw 'Publish failed.'}
[IO.File]::Copy((Join-Path $repoRoot 'Quick-Start.txt'),(Join-Path $appDirectory 'Quick-Start.txt'),$true)
$dotnetRoot = Split-Path $dotnetExecutable -Parent
foreach ($entry in @(@('LICENSE.txt','DotNet-License.txt'),@('ThirdPartyNotices.txt','DotNet-ThirdParty-Notices.txt'))) {
    $license = Join-Path $dotnetRoot $entry[0]
    if (Test-Path -LiteralPath $license) {[IO.File]::Copy($license,(Join-Path $appDirectory $entry[1]),$true)}
}
$temporarySource = Join-Path ([IO.Path]::GetTempPath()) ('FlareLabels-Version-' + [guid]::NewGuid().ToString('N') + '.cs')
[IO.File]::WriteAllText($temporarySource, "using System.Reflection; [assembly: AssemblyVersion(`"$Version.0`")] [assembly: AssemblyFileVersion(`"$Version.0`")] [assembly: AssemblyProduct(`"Flare Labels Setup`")]")
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$icon = Join-Path $repoRoot 'Assets\Flare-Labels.ico'
$logo = Join-Path $repoRoot 'Assets\Flare-Labels.png'
$manifest = Join-Path $repoRoot 'Installer\app.manifest'
$uninstaller = Join-Path $appDirectory 'FlareLabels-Uninstall.exe'
$common = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',"/win32icon:$icon","/win32manifest:$manifest",'/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll')
Push-Location (Join-Path $repoRoot 'Installer')
try {
    & $compiler @common '/define:UNINSTALLER' "/out:$uninstaller" 'Setup.cs' $temporarySource
    if ($LASTEXITCODE -ne 0) {throw 'Uninstaller compilation failed.'}
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $payload = Join-Path $releaseRoot 'payload.zip'
    if (Test-Path -LiteralPath $payload) {Remove-Item -LiteralPath $payload}
    [IO.Compression.ZipFile]::CreateFromDirectory($appDirectory,$payload,[IO.Compression.CompressionLevel]::Optimal,$false)
    $installer = Join-Path $releaseRoot "Flare-Labels-Setup-$Version.exe"
    & $compiler @common "/out:$installer" "/resource:$payload,FlareLabels.Payload" "/resource:$logo,FlareLabels.Logo" 'Setup.cs' $temporarySource
    if ($LASTEXITCODE -ne 0) {throw 'Installer compilation failed.'}
    & $installer --verify-payload
    if ($LASTEXITCODE -ne 0) {throw 'Installer payload verification failed.'}
    $portable = Join-Path $releaseRoot "Flare-Labels-$Version-portable.zip"
    if (Test-Path -LiteralPath $portable) {Remove-Item -LiteralPath $portable}
    [IO.Compression.ZipFile]::CreateFromDirectory($appDirectory,$portable,[IO.Compression.CompressionLevel]::Optimal,$false)
    $summary = [ordered]@{version=$Version;installer=[IO.Path]::GetFileName($installer);installerSize=(Get-Item -LiteralPath $installer).Length;sha256=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant();portable=[IO.Path]::GetFileName($portable)}
    $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseRoot 'build-info.json') -Encoding utf8
    Write-Output "Built Flare Labels $Version in $releaseRoot"
} finally {
    Pop-Location
    if (Test-Path -LiteralPath $temporarySource) {Remove-Item -LiteralPath $temporarySource}
}
