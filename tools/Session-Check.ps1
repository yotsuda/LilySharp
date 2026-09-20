<#
.SYNOPSIS
The start-of-session and end-of-session bookkeeping of docs/HANDOFF.md §0 / §7, as one
command — every number §1 hands over, counted the way §0 defines it, so no session
re-derives (or mis-derives) a recipe.

.DESCRIPTION
  tools/Session-Check.ps1 -Start p431           # ★ THE WHOLE START: verification, then §7 3.5's archive, then what §1 hands over
  tools/Session-Check.ps1 -End p431 -DiffBase 7f4cf2ce  # ★ THE WHOLE END, mechanical half: full run + every ceiling and gate as a table

  tools/Session-Check.ps1                       # git state + every handed-over count
  tools/Session-Check.ps1 -Build                # + solution build (--no-incremental, Core warnings)
  tools/Session-Check.ps1 -Test -Session p353   # + full suite with a trx under ..\LilySharp-Lab\sessions\p353 (host-death check)
  tools/Session-Check.ps1 -DiffBase 7cad95db    # §7.5: Core '+' lines and LILYPOND-REF / LILYSHARP-OWN counts since a base
  tools/Session-Check.ps1 -Archive 350          # §7 3.5: move "## 以下は第350セッションの経緯" verbatim to the top of HANDOFF-ARCHIVE.md

⚠️ -Start ARCHIVES AFTER IT VERIFIES, and that order is load-bearing: the archive leaves §1
with no predecessor block, so running it first reddens two of HandoffArchiveContinuityTests'
assertions in the very run that is supposed to reproduce the last session's total.

The counts and their definitions (RULES §6 「数え方」):
  台帳 = entries of audit/lp-geometry/lp-geometry.json; ss 非ゼロ/総和 exclude unit=count; count 点 counted apart;
  exact = |residual| <= 1e-6 (the ledger's declared tolerance, boundary inclusive); OPEN = why starts with "OPEN:";
  snapshot = git ls-files LilySharp.Tests/Snapshots/*; 追跡 .lys = git ls-files *.lys (audit alone is a different number);
  未追跡 = porcelain lines starting with "??" (NOT -like '??*', whose ? is a wildcard).
#>
param(
    [switch]$Build,
    [switch]$Test,
    [string]$Session,
    [string]$DiffBase,
    [int]$Archive,
    [string]$Start,
    [string]$End
)

# The two rituals, so that neither end of a session is a list of steps to be remembered — and
# so the numbers have ONE implementation. Everything below was already here; -Start and -End
# only say which parts belong to which end, in the order they have to run.
if ($Start) { $Session = $Start; $Build = $true; $Test = $true }
if ($End) { $Session = $End; $Test = $true }

$ErrorActionPreference = 'Continue'
$repo = Split-Path $PSScriptRoot -Parent
# The working notes live in a private sibling repo (git clone https://github.com/yotsuda/LilySharp-Lab next to this one).
$lab = Join-Path (Split-Path $repo -Parent) 'LilySharp-Lab'
Set-Location $repo

function Section([string]$title) { Write-Host ""; Write-Host "== $title" -ForegroundColor Cyan }

$handoff = Join-Path $repo 'docs\HANDOFF.md'

# The seam HandoffArchiveContinuityTests reads, read the same way, so the script and the guard
# cannot drift: §1 carries the current session plus exactly ONE predecessor block.
function HandoffBlocks([string]$path) {
    @([IO.File]::ReadAllLines($path) |
        ForEach-Object { [regex]::Match($_, '^##\s*以下は第(?<n>\d+)セッション') } |
        Where-Object { $_.Success } | ForEach-Object { [int]$_.Groups['n'].Value })
}

# The two ceilings TheHandoffStaysReadable asserts, computed identically (§1's current block is
# the text between the §1 heading and the kept predecessor block).
function HandoffCeilings {
    $bytes = (Get-Item $handoff).Length
    $t = [IO.File]::ReadAllText($handoff)
    $s1 = $t.IndexOf('## 1. 現在地')
    $cur = -1
    if ($s1 -ge 0) {
        $m = [regex]::Match($t.Substring($s1), '^## 以下は第\d+セッション',
            [Text.RegularExpressions.RegexOptions]::Multiline)
        if ($m.Success) { $cur = $m.Index }
    }
    [pscustomobject]@{
        Bytes = $bytes; FileRoom = 450000 - $bytes
        Current = $cur; BlockRoom = 20000 - $cur
    }
}

# §1's next-move list, printed rather than re-read: it is the one thing every session needs
# first, and it used to be written twice (once in each narrative block) and read from the
# middle of a dense paragraph.
function NextMoves {
    $t = [IO.File]::ReadAllText($handoff)
    $m = [regex]::Match($t, '^###\s*1\.0[^\r\n]*\r?\n(?<body>.*?)(?=^###|\Z)',
        [Text.RegularExpressions.RegexOptions]'Multiline,Singleline')
    if ($m.Success) { $m.Groups['body'].Value.Trim() } else { $null }
}

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
if (Test-Path $lab) {
    $scr = @(Get-ChildItem (Join-Path $lab 'sessions') -Directory -ErrorAction SilentlyContinue |
        Where-Object Name -match '^p\d+$' | Sort-Object { [int]$_.Name.Substring(1) } | Select-Object -Last 3 | ForEach-Object Name)
    "Lab sessions の最新: $($scr -join ', ')   (git -C $lab status -sb: $(git -C $lab status -sb | Select-Object -First 1))"
} else { "⚠️ $lab が無い＝作業記録・実コーパスが無い（git clone https://github.com/yotsuda/LilySharp-Lab）" }

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
# The old in-repo scratch\ is excluded so every machine counts the same population.
$disk = @(Get-ChildItem $repo, $lab -Recurse -Filter *.lys -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and -not $_.FullName.StartsWith((Join-Path $repo 'scratch\')) }).Count
"ディスク上の .lys $disk 冊（repo ＋ LilySharp-Lab・掃きの母集団はこちら）"

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
    if (-not $Session) { throw '-Test needs -Session pNNN (the trx lands in ..\LilySharp-Lab\sessions\pNNN\runN.trx)' }
    $dir = Join-Path $lab "sessions\$Session"
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

# ------------------------------------------------- §7 3.5, the number worked out rather than typed
# The block to archive is the one predecessor §1 still holds — the same number the guard reads,
# so no session has to work out "N minus two" and none can archive the wrong block.
if ($Start -and -not $Archive) {
    $blocks = HandoffBlocks $handoff
    if ($blocks.Count -eq 1) {
        $Archive = $blocks[0]
    } elseif ($blocks.Count -eq 0) {
        Section '§7 3.5: 済み'
        "HANDOFF §1 に predecessor ブロックが無い＝既にアーカイブ済み。"
        "§1 を書くとき「## 以下は第Nセッションの経緯」を旧文の上に立てること（N ＝ 直前便）。"
    } else {
        Section '§7 3.5: ⚠️ ブロックが複数'
        "HANDOFF §1 が $($blocks.Count) 個のブロックを持っている（$($blocks -join ', ')）＝前便が displaced せずに append した。"
        "一番古い第$($blocks | Sort-Object | Select-Object -First 1) から順に -Archive で落とすこと。"
        $Archive = ($blocks | Sort-Object | Select-Object -First 1)
    }
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

# ---------------------------------------------------------------- what §1 hands over
if ($Start) {
    $c = HandoffCeilings
    Section '次の一手（HANDOFF §1.0 の逐語）'
    $nm = NextMoves
    if ($nm) { $nm } else {
        "⚠️ §1 に「### 1.0 次の一手」が無い。§1 の語りの中から拾うこと（一覧に直したら次便から刷られる）。"
    }
    Section '天井の残り'
    "HANDOFF.md $($c.Bytes) B / 450,000（残り $($c.FileRoom) B）  §1 現在便 $($c.Current) 字 / 20,000（残り $($c.BlockRoom) 字）"
    if ($c.FileRoom -lt 8000) { "⚠️ 残りが今便ぶん（7〜9 KB 級）を切っている＝tools\Fold-ClosedHandoffItems.ps1 で §2 の閉じた本文を落とす。天井は上げない。" }
    Section '読み方の罠（残り 3 つ・全文は RULES §5.5）'
    "⑴ 緑は語ではなく*合計*で読む（上の trx 行）。⑵ 測る前に `dotnet build LilySharp.Cli -c Release`（既定は Debug）。"
    "⑶ A/B の before はその場で写す（audit\probe-out\pitches.csv はどんな run でも上書きされる）。"
    "⑷ ベンチ・full test の前にユーザーに静かな窓をもらう（CLAUDE-OPERATIONS §2）。"
}

# ---------------------------------------------------------------- §7 の機械的な半分
if ($End) {
    Section '§7 の門（機械が言える分だけ。7.6／7.7／§5.2 片手の読み直しは散文のまま）'
    $rows = [System.Collections.Generic.List[object]]::new()
    function Gate([string]$item, [bool]$ok, [string]$said) {
        $rows.Add([pscustomobject]@{ '§7' = $item; '判定' = $(if ($ok) { 'OK' } else { '⚠️' }); '実測' = $said })
    }

    $c = HandoffCeilings
    Gate '2 天井' (($c.FileRoom -ge 0) -and ($c.BlockRoom -ge 0)) `
        "HANDOFF $($c.Bytes) B（残り $($c.FileRoom)）/ §1 現在便 $($c.Current) 字（残り $($c.BlockRoom)）"

    $hb = HandoffBlocks $handoff
    $ab = HandoffBlocks (Join-Path $repo 'docs\HANDOFF-ARCHIVE.md')
    $seam = ($hb.Count -eq 1) -and ($ab.Count -gt 0) -and
        (($ab | Measure-Object -Maximum).Maximum -eq ($hb | Measure-Object -Maximum).Maximum - 1)
    Gate '3.5 継ぎ目' $seam `
        "§1 が持つブロック $($hb -join ', ') / ARCHIVE の最新 $(($ab | Measure-Object -Maximum).Maximum)"

    # 毒の残骸。計器も毒も便の名前を負っているので、その名前がコードに残っていたら出荷事故。
    $num = $Session -replace '^p', ''
    $left = @(git -c color.grep=never grep -l -E "Zz$num|ZZPOISON" -- 'LilySharp.Core' 'LilySharp.Tests' 'LilySharp.Cli')
    Gate '7 毒と計器' ($left.Count -eq 0) $(if ($left.Count) { "⚠️ 残っている: $($left -join ', ')" } else { "Zz$num / ZZPOISON はコードに 0 件" })

    # 棚卸し。生成器は LILYSHARP_UPDATE_DOCS=1 で回る（ApproximationInventoryTests /
    # MagicConstantInventoryTests）ので、ここは「回した結果が commit 待ちか」だけを見る。
    $inv = @(git status --porcelain -- docs/APPROXIMATIONS.md audit/magic_constants.csv)
    Gate '4 棚卸し' $true $(if ($inv.Count) { "差分あり（$($inv -join ' / ')）＝増減を §1 に書く" } else { '差分なし（LILYSHARP_UPDATE_DOCS=1 で再生成したか確認）' })

    $st2 = @(git status --porcelain)
    $junk = @($st2 | Where-Object { $_ -match '__pycache__|\.trx$|bin/|obj/' })
    Gate '8 作業ツリー' ($junk.Count -eq 0) "$($st2.Count) 項目$(if ($junk.Count) { "・⚠️ 混入: $($junk -join ', ')" })"

    Gate '1 未 push' $true "$ahead 件（push はユーザー。push したと書かない）"

    $rows | Format-Table -AutoSize
    "散文に残る 3 つ: §7 7.5 の読み直し（上の Core '+' と REF/OWN を見て）・7.6 の項ごとの出所・7.7 の匂い一覧。"
}
