$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$directory=Join-Path $projectRoot 'artifacts/tools/InnoSetup'
$compiler=Join-Path $directory 'ISCC.exe'
if(Test-Path -LiteralPath $compiler){Write-Host "Inno Setup: $compiler";return}
$download=Join-Path $projectRoot 'artifacts/tools/innosetup-6.7.4.exe'
New-Item -ItemType Directory -Path (Split-Path $download -Parent) -Force | Out-Null
Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_4/innosetup-6.7.4.exe' -OutFile $download
$expected='A549929319F9120716677C685535CA4671E4B937B5FDC87E2585F32D8EB30E8C'
if((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expected){throw 'Inno Setup download checksum mismatch.'}
$signature=Get-AuthenticodeSignature -LiteralPath $download
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*Pyrsys B.V.*'){throw 'Inno Setup publisher signature is invalid.'}
$process=Start-Process -FilePath $download -ArgumentList ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /NOICONS /DIR="'+$directory+'"') -WindowStyle Hidden -PassThru
if(!$process.WaitForExit(120000) -or $process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $compiler)){throw 'Inno Setup compiler installation failed.'}
Write-Host "Inno Setup: $compiler"
