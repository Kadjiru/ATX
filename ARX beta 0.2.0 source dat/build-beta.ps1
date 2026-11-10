param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\dist\arx-beta'),
    [ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SigningThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$out = [IO.Path]::GetFullPath($OutDir)
if ($SigningThumbprint) {
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$SigningThumbprint" -ErrorAction SilentlyContinue
    if (!$certificate -or !$certificate.HasPrivateKey) { throw 'The requested signing certificate is unavailable.' }
    $signTool = Get-Command signtool.exe -ErrorAction Stop
}

function Sign-PackageFile([string]$File) {
    if (!$SigningThumbprint) { return }
    & $signTool.Source sign /sha1 $SigningThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $File
    if ($LASTEXITCODE) { throw "SignTool failed: $File" }
    $signature = Get-AuthenticodeSignature -LiteralPath $File
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $SigningThumbprint -or
        !$signature.TimeStamperCertificate) { throw "Publisher signature or timestamp could not be verified: $File" }
}
$stage = Join-Path $out 'ARX-1.0.3-beta-win-x64'
New-Item -ItemType Directory -Force $stage | Out-Null

dotnet publish (Join-Path $PSScriptRoot 'ARX.Beta.csproj') -c Release -r win-x64 --self-contained true -o $stage `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:DebugSymbols=false
if ($LASTEXITCODE) { throw "dotnet publish failed ($LASTEXITCODE)" }
Sign-PackageFile (Join-Path $stage 'ARX.Beta.exe')

Copy-Item (Join-Path $PSScriptRoot 'LICENSE'), (Join-Path $PSScriptRoot 'INSTALL.txt'), (Join-Path $PSScriptRoot 'SOURCES.md') $stage
foreach ($path in 'ARX.Beta.exe', 'Data\Crafting.json', 'Data\CatalogManifest.json', 'Data\Reference.json', 'Data\Atlas.json.gz',
    'Data\WorldBosses.json', 'Data\Pets.json', 'Data\MIT-SkeeveTV.txt',
    'GameData\skills\ru.json', 'GameData\NOTICE.md') {
    if (!(Test-Path -LiteralPath (Join-Path $stage $path))) { throw "Missing package file: $path" }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = Join-Path $out 'ARX-1.0.3-beta-win-x64.zip'
$temporary = $archive + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporary, [IO.Compression.CompressionLevel]::Optimal, $false)
    Move-Item -LiteralPath $temporary -Destination $archive -Force
}
finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }

$compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if (!$compiler) {
    Write-Warning 'Inno Setup compiler is unavailable. The portable ZIP is ready.'
    return
}
& $compiler.Source /Qp "/DPayload=$stage" "/O$out" (Join-Path $PSScriptRoot 'ARX.iss')
if ($LASTEXITCODE) { throw "Inno Setup failed ($LASTEXITCODE)" }
Sign-PackageFile (Join-Path $out 'ARX-Setup-1.0.3-beta.exe')
