param([string]$Manifest = (Join-Path (Split-Path -Parent $PSScriptRoot) 'extension/manifest.json'))

$ErrorActionPreference = 'Stop'
$manifestData = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
if ($manifestData.key -notmatch '^[A-Za-z0-9+/]+={0,2}$') {
    throw 'The extension manifest needs a public key for a stable developer-mode ID.'
}
$publicKey = [Convert]::FromBase64String($manifestData.key)
$hash = [Security.Cryptography.SHA256]::HashData($publicKey)
$id = -join ($hash[0..15] | ForEach-Object {
    [char](97 + ($_ -shr 4))
    [char](97 + ($_ -band 15))
})
Write-Output $id
