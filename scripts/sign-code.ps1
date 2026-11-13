param(
 [Parameter(Mandatory)][string[]]$Path,
 [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$CertificateThumbprint,
 [string]$TimestampServer='http://timestamp.digicert.com',
 [string]$SignToolPath
)
$ErrorActionPreference='Stop'
$certificate=Get-Item -LiteralPath "Cert:/CurrentUser/My/$CertificateThumbprint"
if(!$certificate.HasPrivateKey){throw 'The selected signing certificate has no accessible private key.'}
if($certificate.Subject -eq $certificate.Issuer){throw 'Self-signed certificates are not accepted for release signing.'}
if($certificate.NotAfter -le (Get-Date) -or $certificate.NotBefore -gt (Get-Date)){throw 'The signing certificate is outside its validity period.'}
if(!($certificate.EnhancedKeyUsageList | Where-Object {$_.ObjectId -eq '1.3.6.1.5.5.7.3.3'})){throw 'The certificate must have the Code Signing EKU.'}
if(!$certificate.Verify()){throw 'The signing certificate chain is not trusted or could not be verified.'}
$timestampUri=[uri]$TimestampServer
if(!$timestampUri.IsAbsoluteUri -or $timestampUri.Scheme -notin @('http','https')){throw 'A valid RFC 3161 timestamp server URL is required.'}
if(!$SignToolPath){
 $sdk=Get-ChildItem 'C:/Program Files (x86)/Windows Kits/10/bin' -Directory | Where-Object {$_.Name -match '^10\.' -and (Test-Path -LiteralPath "$($_.FullName)/x64/signtool.exe")} | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
 if(!$sdk){throw 'Windows SDK SignTool was not found.'}
 $SignToolPath=Join-Path $sdk.FullName 'x64/signtool.exe'
}
foreach($file in $Path){
 $resolved=(Resolve-Path -LiteralPath $file).Path
 & $SignToolPath sign /fd SHA256 /sha1 $CertificateThumbprint /tr $TimestampServer /td SHA256 $resolved
 if($LASTEXITCODE){throw "Signing failed: $resolved"}
 & $SignToolPath verify /pa /all /tw $resolved
 if($LASTEXITCODE){throw "Signature or timestamp verification failed: $resolved"}
 $signature=Get-AuthenticodeSignature -LiteralPath $resolved
 if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint -or !$signature.TimeStamperCertificate){throw "Unexpected signer, missing timestamp or untrusted signature: $resolved"}
}
