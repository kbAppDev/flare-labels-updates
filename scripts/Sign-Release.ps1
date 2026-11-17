param([Parameter(Mandatory=$true)][string]$Version)
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid version.'}
$repoRoot=Split-Path $PSScriptRoot -Parent
$releaseRoot=Join-Path $repoRoot "dist\v$Version"
$installer=Join-Path $releaseRoot "Flare-Labels-Setup-$Version.exe"
$dll=Join-Path $repoRoot 'bin\Release\net10.0-windows\win-x64\FlareLabels.dll'
$dotnetExecutable=if($env:DOTNET_ROOT){Join-Path $env:DOTNET_ROOT 'dotnet.exe'}else{(Get-Command dotnet).Source}
$payload=[ordered]@{schema=1;application='FlareLabels';version=$Version;installerUrl="https://github.com/kbAppDev/flare-labels-updates/releases/download/v$Version/Flare-Labels-Setup-$Version.exe";installerSize=(Get-Item -LiteralPath $installer).Length;sha256=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()}
$payloadPath=Join-Path $releaseRoot 'update-payload.json'
$outputPath=Join-Path $releaseRoot 'flare-labels-update.json'
[IO.File]::WriteAllText($payloadPath,($payload|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
& $dotnetExecutable $dll --sign-update $payloadPath $outputPath
if($LASTEXITCODE -ne 0){throw 'Release signing failed.'}
& $dotnetExecutable $dll --verify-update $outputPath $installer
if($LASTEXITCODE -ne 0){throw 'Signed release verification failed.'}
Write-Output 'Signed installer manifest verified against the public key embedded in the app.'
