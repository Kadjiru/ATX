param([string]$OutDir = (Join-Path $PSScriptRoot '..\dist\arx-beta'))

$ErrorActionPreference = 'Stop'
$out = [IO.Path]::GetFullPath($OutDir)
$stage = Join-Path $out 'ARX-0.1.0-beta-win-x64'
New-Item -ItemType Directory -Force $stage | Out-Null

dotnet publish (Join-Path $PSScriptRoot 'ARX.Beta.csproj') -c Release -r win-x64 --self-contained true -o $stage `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:DebugSymbols=false
if ($LASTEXITCODE) { throw "dotnet publish failed ($LASTEXITCODE)" }

Copy-Item (Join-Path $PSScriptRoot 'LICENSE'), (Join-Path $PSScriptRoot 'INSTALL.txt'), (Join-Path $PSScriptRoot 'SOURCES.md') $stage
foreach ($path in 'ARX.Beta.exe', 'Data\Crafting.json', 'Data\CatalogManifest.json', 'Data\Reference.json') {
    if (!(Test-Path -LiteralPath (Join-Path $stage $path))) { throw "Missing package file: $path" }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = Join-Path $out 'ARX-0.1.0-beta-win-x64.zip'
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
