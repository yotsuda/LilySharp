# TEMPORARY — session 136 ⒟⁶. Asks every book in the tree whether the preliminary pass's
# beams and the final pass's beams are the same beams (HANDOFF §5.0: ask the corpus, do not
# reason). Single process, single thread — the checker's state is static.
param([string]$Sink = 'C:\MyProj\LilySharp\audit\lpreg\beamcheck-136.txt')
$ErrorActionPreference = 'Continue'
$lysc = 'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net9.0\lysc.dll'
if (-not (Test-Path $lysc)) { throw "build Release first: $lysc" }
if (Test-Path $Sink) { Remove-Item $Sink }
$env:LILYSHARP_STAGE_TIMING = $Sink
$env:LILYSHARP_BEAM_DOUBLECHECK = '1'

$books = @()
$books += Get-ChildItem -Recurse -Filter *.lys 'C:\MyProj\LilySharp\LilySharp.Tests\Fixtures'
$books += Get-ChildItem -Recurse -Filter *.lys 'C:\MyProj\LilySharp\samples' -EA SilentlyContinue
$books += Get-ChildItem -Filter *.lys 'C:\MyProj\LilySharp\audit\lpreg'
"本 $($books.Count) 冊"

$failed = 0
foreach ($b in $books) {
    & dotnet $lysc svg --no-embed-font -o "$env:TEMP\bc136.svg" $b.FullName 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { $failed++ }
}
Remove-Item Env:\LILYSHARP_STAGE_TIMING, Env:\LILYSHARP_BEAM_DOUBLECHECK

$lines = Get-Content $Sink
$summaries = $lines | Select-String 'beamcheck-summary'
$mismatchLines = $lines | Select-String 'beamcheck ' | Where-Object { $_ -notmatch 'summary' }
$compared = 0; $mismatched = 0; $missing = 0; $beams = 0
foreach ($s in $summaries) {
    if ($s -match 'compared=(\d+) mismatched=(\d+) prelimMissing=(\d+) beams=(\d+)') {
        $compared += [int]$Matches[1]; $mismatched += [int]$Matches[2]
        $missing += [int]$Matches[3]; $beams += [int]$Matches[4]
    }
}
"レイアウト回数 $($summaries.Count) / 描画失敗 $failed"
"比較した staff $compared / 不一致 $mismatched / prelim 欠 $missing / 梁 $beams 本"
if ($mismatchLines) { "--- 不一致の先頭 20:"; $mismatchLines | Select-Object -First 20 | ForEach-Object { "  $_" } }
