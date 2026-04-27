<#
M-1: One-shot rewrite of LILYPOND-REF citations whose target file no longer
exists in lilypond-src 2.25.35. Drives audit/citation_drift.csv FileMissing
entries to OK.

Mappings derived from manual investigation against current LP source:

  lily/note-collision-interface.cc        -> lily/note-collision.cc
  lily/grace-spacing.cc                    -> lily/grace-spacing-engraver.cc
  lily/dots.cc                             -> lily/dots-engraver.cc
  lily/lyric-extender-engraver.cc          -> lily/extender-engraver.cc
  lily/skyline.hh                          -> lily/include/skyline.hh
  lily/spacing-determine-shortest-duration-op.cc -> lily/spacing-spanner.cc
  lily/trill-spanner-engraver.cc           -> scm/scheme-engravers.scm  (Trill_spanner_engraver definition)
  lily/glissando-engraver.cc               -> scm/scheme-engravers.scm  (Glissando_engraver definition; geometry in lily/line-spanner.cc)
#>
param(
    [string]$LilySharpRoot = 'C:\MyProj\LilySharp\LilySharp.Core'
)

$ErrorActionPreference = 'Stop'

$mappings = @(
    @{ From = 'lily/note-collision-interface.cc'; To = 'lily/note-collision.cc' },
    @{ From = 'lily/grace-spacing.cc';            To = 'lily/grace-spacing-engraver.cc' },
    @{ From = 'lily/dots.cc';                     To = 'lily/dots-engraver.cc' },
    @{ From = 'lily/lyric-extender-engraver.cc';  To = 'lily/extender-engraver.cc' },
    @{ From = 'lily/skyline.hh';                  To = 'lily/include/skyline.hh' },
    @{ From = 'lily/spacing-determine-shortest-duration-op.cc'; To = 'lily/spacing-spanner.cc' },
    @{ From = 'lily/trill-spanner-engraver.cc';   To = 'scm/scheme-engravers.scm' },
    @{ From = 'lily/glissando-engraver.cc';       To = 'scm/scheme-engravers.scm' }
)

$files = Get-ChildItem -Path $LilySharpRoot -Recurse -Filter *.cs -File `
    | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

$changedFiles = 0
$totalReplacements = 0
foreach ($f in $files) {
    $orig = [System.IO.File]::ReadAllText($f.FullName)
    $cur  = $orig
    foreach ($m in $mappings) {
        $from = [regex]::Escape($m.From)
        $to   = $m.To
        $cur = [regex]::Replace($cur, $from, $to)
    }
    if ($cur -ne $orig) {
        # Preserve original encoding
        $bom = [System.Text.Encoding]::UTF8.GetPreamble()
        $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
        $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
        $encoding = if ($hasBom) { New-Object System.Text.UTF8Encoding($true) } else { New-Object System.Text.UTF8Encoding($false) }
        [System.IO.File]::WriteAllText($f.FullName, $cur, $encoding)
        $diff = ($cur.Length - $orig.Length)
        $count = 0
        foreach ($m in $mappings) {
            $count += ([regex]::Matches($orig, [regex]::Escape($m.From))).Count
        }
        $totalReplacements += $count
        $changedFiles++
        Write-Host "  $($f.FullName.Substring($LilySharpRoot.Length+1)): $count replacements"
    }
}
Write-Host ""
Write-Host "Changed $changedFiles files, $totalReplacements total replacements"
