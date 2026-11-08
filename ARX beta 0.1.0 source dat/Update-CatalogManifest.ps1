param([Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Revision)

$ErrorActionPreference = 'Stop'
$data = Join-Path $PSScriptRoot 'Data'
$utf8 = [Text.UTF8Encoding]::new($false)

function Get-CatalogDigest([string]$Name) {
    $payload = [IO.File]::ReadAllText((Join-Path $data $Name)).Replace("`r`n", "`n")
    $bytes = $utf8.GetBytes($payload)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

$manifest = [ordered]@{
    Schema = 1
    Revision = $Revision
    CraftingSha256 = Get-CatalogDigest 'Crafting.json'
    ReferenceSha256 = Get-CatalogDigest 'Reference.json'
}
[IO.File]::WriteAllText((Join-Path $data 'CatalogManifest.json'), ($manifest | ConvertTo-Json), $utf8)
