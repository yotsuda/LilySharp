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

  tools/Fold-ClosedHandoffItems.ps1            # fold
  tools/Fold-ClosedHandoffItems.ps1 -WhatIf    # count only
#>
param([switch]$WhatIf, [string]$Session = '')
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
function Enc([string]$p) { $b = [IO.File]::ReadAllBytes($p); New-Object System.Text.UTF8Encoding(($b[0] -eq 0xEF -and $b[1] -eq 0xBB)) }
function Nl([string]$s) { if ($s.Contains("`r`n")) { "`r`n" } else { "`n" } }

$hp = 'docs\HANDOFF.md'; $ap = 'docs\HANDOFF-ARCHIVE.md'
$he = Enc $hp; $ae = Enc $ap
$h = [IO.File]::ReadAllText($hp, $he); $a = [IO.File]::ReadAllText($ap, $ae)
$hn = Nl $h; $an = Nl $a
$stamp = (Get-Date -Format 'yyyy-MM-dd') + $(if ($Session) { "・第$Session" } else { '' })

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
"folded $folded closed items, $movedLines body lines"
if ($WhatIf -or $folded -eq 0) { return }

$h = $h.Substring(0, $s2) + ($out -join "`n") + $h.Substring($s3)
$block = ((($moved -join "`n") -replace "`r", '') -replace "`n", $an)
$marker = '## 閉じた §2 の本文'
if ($a.IndexOf($marker) -lt 0) {
  if (-not $a.EndsWith($an)) { $a += $an }
  $a += "$an$an$marker（HANDOFF §2 から逐語で落とした本文。見出しは §2 の項目の 1 行目・ポインタは §2 に残る）$an$an"
}
if (-not $a.EndsWith($an)) { $a += $an }
$a += $block + $an
[IO.File]::WriteAllText($hp, $h, $he)
[IO.File]::WriteAllText($ap, $a, $ae)
"HANDOFF $((Get-Item $hp).Length) bytes / ARCHIVE $((Get-Item $ap).Length) bytes"
