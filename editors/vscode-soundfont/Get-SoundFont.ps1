# Fetches MuseScore_General.sf3 from the MuseScore project's download mirror into soundfont/, and
# checks that it is the file this package says it carries. Run before `vsce package`.
# The file is not kept in git (40 MB); it is carried unchanged, so the hash is the whole check.
$ErrorActionPreference = 'Stop'
$url = 'https://ftp.osuosl.org/pub/musescore/soundfont/MuseScore_General/MuseScore_General.sf3'
$sha256 = '5b85b6c2c61d10b2b91cddd41efcce7b25cd31c8271d511c73afafbef20b6fa3'
$folder = Join-Path $PSScriptRoot 'soundfont'
$file = Join-Path $folder 'MuseScore_General.sf3'
New-Item -ItemType Directory -Force $folder | Out-Null
if (-not (Test-Path $file)) {
    Write-Host "Fetching $url"
    Invoke-WebRequest -Uri $url -OutFile $file
}
$got = (Get-FileHash $file -Algorithm SHA256).Hash.ToLower()
if ($got -ne $sha256) {
    Remove-Item $file
    throw "MuseScore_General.sf3 is not the file expected (SHA-256 $got, expected $sha256): removed. The mirror may carry a newer version: read its changelog, and change the hash, the README and the version together."
}
Write-Host "soundfont/MuseScore_General.sf3 is in place ($([int]((Get-Item $file).Length / 1MB)) MB, SHA-256 as expected)."