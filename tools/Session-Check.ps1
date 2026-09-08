<#
.SYNOPSIS
The start-of-session and end-of-session bookkeeping of docs/HANDOFF.md §0 / §7, as one
command — every number §1 hands over, counted the way §0 defines it, so no session
re-derives (or mis-derives) a recipe.

.DESCRIPTION
  tools/Session-Check.ps1                       # git state + every handed-over count
  tools/Session-Check.ps1 -Build                # + solution build (--no-incremental, Core warnings)
  tools/Session-Check.ps1 -Test -Scratch p353   # + full suite with a trx under scratch/p353 (host-death check)
  tools/Session-Check.ps1 -DiffBase 7cad95db    # §7.5: Core '+' lines and LILYPOND-REF / LILYSHARP-OWN counts since a base
  tools/Session-Check.ps1 -Archive 350          # §7 3.5: move "## 以下は第350セッションの経緯" verbatim to the top of HANDOFF-ARCHIVE.md

The counts and their definitions (RULES §6 「数え方」):
  台帳 = entries of audit/lp-geometry/lp-geometry.json; ss 非ゼロ/総和 exclude unit=count; count 点 counted apart;
  exact = |residual| <= 1e-6 (the ledger's declared tolerance, boundary inclusive); OPEN = why starts with "OPEN:";
  snapshot = git ls-files LilySharp.Tests/Snapshots/*; 追跡 .lys = git ls-files *.lys (audit alone is a different number);
  未追跡 = porcelain lines starting with "??" (NOT -like '??*', whose ? is a wildcard).
#>
param(
    [switch]$Build,
    [switch]$Test,
    [string]$Scratch,
    [string]$DiffBase,
    [int]$Archive
)

$ErrorActionPreference = 'Continue'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo

function Section([string]$title) { Write-Host ""; Write-Host "== $title" -ForegroundColor Cyan }

# ---------------------------------------------------------------- git
Section 'git'
git --no-pager log --oneline -5
$head = (git rev-parse --short=8 HEAD)
$origin = (git --no-pager log --oneline -1 origin/master)
$ahead = git rev-list --count origin/master..master
$st = @(git status --porcelain)
$untracked = @($st | Where-Object { $_.StartsWith('??') }).Count
"HEAD $head / 未push $ahead / origin/master: $origin"
"未追跡 $untracked / 作業ツリー項目 $($st.Count)"
if ($st.Count) { $st | ForEach-Object { "  $_" } }
$tfm = @(Get-ChildItem LilySharp.Cli\bin\Debug -Directory -ErrorAction SilentlyContinue | ForEach-Object Name)
"lysc TFM dirs: $($tfm -join ', ')   (net10.0 だけが生きている・化石が並んだら消す)"
$scr = @(Get-ChildItem scratch -Directory -Filter 'p3*' -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 3 | ForEach-Object Name)
"scratch の最新: $($scr -join ', ')"

# ---------------------------------------------------------------- counts
Section 'counts (§0 の数え方)'
$ledger = Get-Content audit\lp-geometry\lp-geometry.json -Raw | ConvertFrom-Json
$e = @($ledger.entries.PSObject.Properties)
$nz = @($e | Where-Object { $_.Value.residual -ne 0 -and $_.Value.unit -ne 'count' })
$sum = (($nz | ForEach-Object { [math]::Abs($_.Value.residual) }) | Measure-Object -Sum).Sum
$c = @($e | Where-Object { $_.Value.unit -eq 'count' })
$exact = @($e | Where-Object { [math]::Abs($_.Value.residual) -le 1e-6 }).Count
$open = @($e | Where-Object { $_.Value.why -like 'OPEN:*' }).Count
"台帳 $($e.Count) 点 / ss 非ゼロ $($nz.Count) 総和 $([math]::Round($sum, 9)) / count 点 $($c.Count) うち非ゼロ $(@($c | Where-Object { $_.Value.residual -ne 0 }).Count) / exact(<=1e-6) $exact / OPEN: $open"
"snapshot $(@(git ls-files 'LilySharp.Tests/Snapshots/*').Count) 枚 / 追跡 .lys $(@(git ls-files '*.lys').Count) 冊 (audit 配下 $(@(git ls-files 'audit/*.lys').Count))"
$disk = @(Get-ChildItem -Recurse -Filter *.lys -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }).Count
"ディスク上の .lys $disk 冊（scratch 込み・掃きの母集団はこちら）"

# ---------------------------------------------------------------- CI
Section 'CI (gh run list)'
if (Get-Command gh -ErrorAction SilentlyContinue) { gh run list --limit 5 } else { "gh not on PATH" }

# ---------------------------------------------------------------- build
if ($Build) {
    Section 'build (solution, --no-incremental; Core は 0 警告が期待値)'
    $out = dotnet build LilySharp.slnx --no-incremental -v q 2>&1
    $out | Select-String 'エラー|error|LilySharp\.Core.*warning' | ForEach-Object { $_.Line }
    "BUILD EXIT $LASTEXITCODE"
}

# ---------------------------------------------------------------- test
if ($Test) {
    if (-not $Scratch) { throw '-Test needs -Scratch pNNN (the trx lands in scratch/pNNN/runN.trx)' }
    $dir = Join-Path $repo "scratch\$Scratch"
    New-Item -ItemType Directory -Force $dir | Out-Null
    $n = 1; while (Test-Path (Join-Path $dir "run$n.trx")) { $n++ }
    $trx = Join-Path $dir "run$n.trx"
    Section "test (full, trx = $trx)"
    $out = dotnet test LilySharp.Tests\LilySharp.Tests.csproj -v q --logger "trx;LogFileName=$trx" 2>&1
    $out | Select-String '成功!|失敗!|Passed!|Failed!' | ForEach-Object { $_.Line }
    "TEST EXIT $LASTEXITCODE"
    if (Test-Path $trx) {
        [xml]$x = Get-Content $trx -Raw
        $r = @($x.TestRun.Results.UnitTestResult)
        $failed = @($r | Where-Object { $_.outcome -eq 'Failed' })
        $skipped = @($r | Where-Object { $_.outcome -eq 'NotExecuted' }).Count
        "trx: 合格 $(@($r | Where-Object { $_.outcome -eq 'Passed' }).Count) / 失敗 $($failed.Count) / skip $skipped / 合計 $($r.Count)   ← 引継ぎの合計と突き合わせること"
        $failed | ForEach-Object { "  FAILED $($_.testName)" }
        $info = $x.TestRun.ResultSummary.RunInfos.RunInfo
        if ($info) { "⚠️ RunInfos（ホストの死など）:"; @($info) | ForEach-Object { "  $($_.Text)" } }
    }
}

# ---------------------------------------------------------------- §7.5 diff
if ($DiffBase) {
    Section "§7.5: Core diff since $DiffBase"
    $d = @(git -c color.ui=false diff $DiffBase HEAD -- LilySharp.Core)
    $plus = @($d | Where-Object { $_ -match '^\+' -and $_ -notmatch '^\+\+\+' })
    "Core '+' $($plus.Count) 行 / LILYPOND-REF $(@($plus | Where-Object { $_ -match 'LILYPOND-REF' }).Count) / LILYSHARP-OWN $(@($plus | Where-Object { $_ -match 'LILYSHARP-OWN' }).Count)"
    "  （0 本や 1 本なら、そこが監査対象・RULES §7.5）"
}

# ---------------------------------------------------------------- §7 3.5 archive
if ($Archive) {
    Section "§7 3.5: HANDOFF §1 の第$Archive のブロックを ARCHIVE の先頭へ（バイト保存）"
    $hp = Join-Path $repo 'docs\HANDOFF.md'; $ap = Join-Path $repo 'docs\HANDOFF-ARCHIVE.md'
    function Enc([string]$p) { $b = [IO.File]::ReadAllBytes($p); New-Object System.Text.UTF8Encoding(($b[0] -eq 0xEF -and $b[1] -eq 0xBB)) }
    $he = Enc $hp; $ae = Enc $ap
    $h = [IO.File]::ReadAllText($hp, $he)
    $s = $h.IndexOf("## 以下は第${Archive}セッションの経緯")
    $e2 = $h.IndexOf('## 2. 開いている作業')
    if ($s -lt 0 -or $e2 -le $s) { throw "markers not found in HANDOFF.md: block=$s §2=$e2" }
    $block = $h.Substring($s, $e2 - $s)
    $a = [IO.File]::ReadAllText($ap, $ae)
    $m = [regex]::Match($a, '(?m)^## 以下は第\d+セッション')
    if (-not $m.Success) { throw 'archive marker not found' }
    [IO.File]::WriteAllText($hp, $h.Substring(0, $s) + $h.Substring($e2), $he)
    [IO.File]::WriteAllText($ap, $a.Substring(0, $m.Index) + $block + $a.Substring($m.Index), $ae)
    "moved: $(($block -split "`n").Count) lines, $($block.Length) chars. 次: §1 の旧文の上に「## 以下は第$($Archive + 1)セッションの経緯」を立ててから full run（HandoffArchiveContinuityTests が継ぎ目を見る）"
}
