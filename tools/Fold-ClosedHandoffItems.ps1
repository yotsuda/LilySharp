<#
.SYNOPSIS
Moves the BODY of every closed ("✅") top-level item of docs/HANDOFF.md §2 to the end of
docs/HANDOFF-ARCHIVE.md, verbatim, leaving the item's first line in §2 as a pointer.

.DESCRIPTION
§2 is "open work", but closed items kept their whole history there and the file grew to
4,300 lines (session 351 folded 64 items / 1,923 lines in one go). This is the repeatable
form of that fold: run it when HandoffArchiveContinuityTests says HANDOFF.md is over its
ceiling, or whenever a §2 item closes. Idempotent — an item whose body is already gone
(only its pointer line remains) is skipped. Bytes are preserved (encoding, BOM, CRLF).

An item is "closed" when its first line starts with "- " and contains ✅ but neither ▶ nor
"起票（". Its body = the following lines up to the next top-level bullet or heading.

§3「決定済み」も畳む（2026-09-16 に足した）。§3 は箇条書きではなく表なので、単位は行ではなく
2 列目＝根拠セル: -Before より古い日付の行の根拠だけを ARCHIVE へ逐語で落とし、1 列目（決定・
日付・★）は §3 に残してポインタを置く。表の仕事は「蒸し返さない」ことで、それは主張が残れば
足りる。既定の -Before は今月 1 日。日付の読めない行は畳まない（古いと判断する根拠が無い）。

  tools/Fold-ClosedHandoffItems.ps1                     # fold §2 bodies + old §3 rationale
  tools/Fold-ClosedHandoffItems.ps1 -WhatIf             # count only
  tools/Fold-ClosedHandoffItems.ps1 -Before 2026-08-01  # keep 2026-08 onward inline
#>
param([switch]$WhatIf, [string]$Session = '', [string]$Before = '')
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
# ⚠️ .NET resolves a RELATIVE path against the PROCESS CWD, which Set-Location does not move,
# so every [IO.File] call below threw "Could not find a part of the path '<home>\docs\HANDOFF.md'"
# whenever the shell started anywhere but the repo (measured 2026-09-16 — the script could not
# run at all from a session whose console opened in the user's home).
[Environment]::CurrentDirectory = $repo
function Enc([string]$p) { $b = [IO.File]::ReadAllBytes($p); New-Object System.Text.UTF8Encoding(($b[0] -eq 0xEF -and $b[1] -eq 0xBB)) }
function Nl([string]$s) { if ($s.Contains("`r`n")) { "`r`n" } else { "`n" } }

$hp = 'docs\HANDOFF.md'; $ap = 'docs\HANDOFF-ARCHIVE.md'
$he = Enc $hp; $ae = Enc $ap
$h = [IO.File]::ReadAllText($hp, $he); $a = [IO.File]::ReadAllText($ap, $ae)
$hn = Nl $h; $an = Nl $a
$stamp = (Get-Date -Format 'yyyy-MM-dd') + $(if ($Session) { "・第$Session" } else { '' })
if (-not $Before) { $Before = (Get-Date -Format 'yyyy-MM-01') }

$s2 = $h.IndexOf('## 2. 開いている作業'); $s3 = $h.IndexOf('## 3. 決定済み')
if ($s2 -lt 0 -or $s3 -le $s2) { throw "§2/§3 markers: $s2 $s3" }
$lines = $h.Substring($s2, $s3 - $s2) -split "`n"
$out = New-Object System.Collections.Generic.List[string]
$moved = New-Object System.Collections.Generic.List[string]
$section = ''; $i = 0; $folded = 0; $movedLines = 0
while ($i -lt $lines.Count) {
  $l = $lines[$i]
  if ($l -match '^###\s+(\S+)') { $section = $Matches[1].TrimEnd('.') }
  $closed = ($l -match '^- ') -and ($l -match '✅') -and ($l -notmatch '▶|起票\（')
  if (-not $closed) { $out.Add($l); $i++; continue }
  $j = $i + 1
  while ($j -lt $lines.Count -and -not ($lines[$j] -match '^- ' -or $lines[$j] -match '^#{2,4} ')) { $j++ }
  $k = $j
  while ($k - 1 -gt $i -and $lines[$k - 1].Trim() -eq '') { $k-- }
  if ($k -le $i + 1) { $out.Add($l); $i++; continue }
  $bodyLines = @($lines[($i + 1)..($k - 1)])
  $label = ($l -replace '^- ', '') -replace '\*', ''
  if ($label.Length -gt 110) { $label = $label.Substring(0, 110) + '…' }
  $eol = if ($l.EndsWith("`r")) { "`r" } else { '' }
  $out.Add($l.TrimEnd() + " → **本文は ``HANDOFF-ARCHIVE.md``「閉じた §2 の本文」の同じ見出し**（$stamp に落とした）$eol")
  for ($t = $k; $t -lt $j; $t++) { $out.Add($lines[$t]) }
  $moved.Add("#### [$section] $label"); $moved.Add('')
  foreach ($b in $bodyLines) { $moved.Add($b) }
  $moved.Add('')
  $folded++; $movedLines += $bodyLines.Count
  $i = $j
}
$h = $h.Substring(0, $s2) + ($out -join "`n") + $h.Substring($s3)

# --- §3「決定済み」: the RATIONALE cell of old rows leaves; the claim stays -------------
# ⚠️ A table row's separator is a pipe that is neither ESCAPED (`\|`) nor inside a CODE SPAN.
# §3 writes `|:` and `:|` as prose inside backticks, raw — splitting on unescaped pipes alone
# shreds those rows, and a naive $cells[2] then carries a FRAGMENT to the archive and drops
# the rest of the line. Measured 2026-09-16: of the 43 §3 rows before the cutoff, 7 split
# wrong that way, and the first run of this fold mangled them before it was reverted.
# ⇒ mask code spans, then split; and fold ONLY when exactly three separators are found. With
# the masking 1 row is still unreadable — a genuine THREE-column row — and it is passed
# through untouched and counted, never guessed at.
# ⚠️ TAIL: text after the closing pipe is NOT decoration. One §3 row carries two
# `<!-- ledger: NAME = VALUE -->` citations there, and HandoffLedgerCitationTests parses that
# exact tag and fails when the number drifts from the LP geometry ledger. Rebuilding the row
# without the tail deletes a live citation, so the tail is carried through verbatim and the
# caller's identity check covers it (measured 2026-09-16: 1 of the 43 rows in range).
function RowCells([string]$t) {
  $m = [regex]::Replace($t, '`[^`]*`', { param($x) '~' * $x.Value.Length })
  $pos = New-Object System.Collections.Generic.List[int]
  for ($i = 0; $i -lt $m.Length; $i++) {
    if ($m[$i] -eq '|' -and ($i -eq 0 -or $m[$i - 1] -ne '\')) { $pos.Add($i) }
  }
  if ($pos.Count -ne 3) { return $null }
  $c = @{ C1 = $t.Substring($pos[0] + 1, $pos[1] - $pos[0] - 1)
          C2 = $t.Substring($pos[1] + 1, $pos[2] - $pos[1] - 1)
          Tail = $t.Substring($pos[2] + 1) }
  # The row must be exactly what its parts say it is, or this fold does not touch it.
  if (('|' + $c.C1 + '|' + $c.C2 + '|' + $c.Tail) -ne $t) { return $null }
  $c
}
$folded3 = 0; $moved3Bytes = 0; $skipped3 = 0
$moved3 = New-Object System.Collections.Generic.List[string]
$s3b = $h.IndexOf('## 3. 決定済み')
$after = $h.IndexOf("`n## ", $s3b + 5)
$e3 = if ($after -lt 0) { $h.Length } else { $after + 1 }
$out3 = New-Object System.Collections.Generic.List[string]
foreach ($l in (($h.Substring($s3b, $e3 - $s3b)) -split "`n")) {
  $eol = if ($l.EndsWith("`r")) { "`r" } else { '' }
  $t = $l.TrimEnd("`r")
  $d = [regex]::Match($t, '20\d\d-\d\d-\d\d')
  $wanted = $t.StartsWith('| ') -and $t -notmatch '^\|\s*---' -and $t -notmatch '^\| 決定 ' `
            -and $d.Success -and $d.Value -lt $Before
  if (-not $wanted) { $out3.Add($l); continue }
  $c = RowCells $t
  if ($null -eq $c) { $out3.Add($l); $skipped3++; continue }
  if ($c.C2 -match 'HANDOFF-ARCHIVE\.md') { $out3.Add($l); continue }
  $label = ($c.C1 -replace '\*', '').Trim()
  if ($label.Length -gt 110) { $label = $label.Substring(0, 110) + '…' }
  $moved3.Add("#### $label"); $moved3.Add(''); $moved3.Add($c.C2.Trim()); $moved3.Add('')
  $moved3Bytes += [Text.Encoding]::UTF8.GetByteCount($c.C2)
  $out3.Add('|' + $c.C1 + '| **根拠は `HANDOFF-ARCHIVE.md`「閉じた §3 の根拠」の同じ見出し**（' + $stamp + ' に落とした） |' + $c.Tail + $eol)
  $folded3++
}
$h = $h.Substring(0, $s3b) + ($out3 -join "`n") + $h.Substring($e3)

"folded $folded closed §2 items, $movedLines body lines; $folded3 §3 rationale cells, $moved3Bytes bytes (before $Before); $skipped3 rows left alone (this splitter could not read them)"
if ($WhatIf -or ($folded + $folded3) -eq 0) { return }

if ($moved.Count -gt 0) {
  $block = ((($moved -join "`n") -replace "`r", '') -replace "`n", $an)
  $marker = '## 閉じた §2 の本文'
  if ($a.IndexOf($marker) -lt 0) {
    if (-not $a.EndsWith($an)) { $a += $an }
    $a += "$an$an$marker（HANDOFF §2 から逐語で落とした本文。見出しは §2 の項目の 1 行目・ポインタは §2 に残る）$an$an"
  }
  if (-not $a.EndsWith($an)) { $a += $an }
  $a += $block + $an
}
if ($moved3.Count -gt 0) {
  $block3 = ((($moved3 -join "`n") -replace "`r", '') -replace "`n", $an)
  $marker3 = '## 閉じた §3 の根拠'
  if ($a.IndexOf($marker3) -lt 0) {
    if (-not $a.EndsWith($an)) { $a += $an }
    $a += "$an$an$marker3（HANDOFF §3 の表から逐語で落とした根拠セル。見出しは決定セル・ポインタは §3 に残る）$an$an"
  }
  if (-not $a.EndsWith($an)) { $a += $an }
  $a += $block3 + $an
}
[IO.File]::WriteAllText($hp, $h, $he)
[IO.File]::WriteAllText($ap, $a, $ae)
"HANDOFF $((Get-Item $hp).Length) bytes / ARCHIVE $((Get-Item $ap).Length) bytes"
