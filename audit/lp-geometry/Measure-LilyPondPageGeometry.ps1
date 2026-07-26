<#
.SYNOPSIS
    Runs the page-vertical probe through real LilyPond and prints the page geometry it made.

.DESCRIPTION
    The vertical twin of Measure-LilyPondGeometry.ps1. That one measures anchor-to-anchor
    distances inside a system; this one measures the page — where the first system's ink
    starts below the paper edge, how far apart consecutive systems sit, how much slack is
    left at the foot, and how many systems the breaker put on each page.

    Two regimes are printed separately and must stay separate (HANDOFF 5.3): book N is
    ragged-bottom, where every gap is the spring's natural length, and book J is LilyPond's
    justified default, where the gaps are whatever force the breaker solved for.

    Everything is in staff spaces. See the header of probes/page-vertical.ly for why.

.PARAMETER Probe
    Probe file under probes/. Defaults to page-vertical.ly.

.PARAMETER LilyPond
    Path to lilypond.exe.

.NOTES
    Guile deadlocks when LilyPond is launched with an inherited console, so the run is
    detached via `cmd /c "... < NUL"`, and stdout/stderr are kept on separate files —
    merged, an unbuffered diagnostic lands in the middle of a dump line. Both hazards are
    documented at length in the X script; they apply here unchanged.

    Unlike the X probe this one does NOT pass -dbackend=null: the Y-offsets it reads are
    filled in by Page::page_stencil, which only runs when the pages are actually realized
    (lily/paper-book.cc:775-788 calls page-post-process right after it). Output goes to a
    temp dir and is thrown away.

.EXAMPLE
    pwsh audit/lp-geometry/Measure-LilyPondPageGeometry.ps1
#>
[CmdletBinding()]
param(
    [string] $Probe = 'page-vertical.ly',
    [string] $LilyPond = 'C:\bin\lilypond-2.26.0\bin\lilypond.exe'
)

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$probePath = Join-Path $here 'probes' $Probe
if (-not (Test-Path $probePath)) { throw "probe not found: $probePath" }
if (-not (Test-Path $LilyPond)) { throw "lilypond.exe not found: $LilyPond — pass -LilyPond" }

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("lp-page-" + [System.IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $out = Join-Path $work 'out.txt'
    $err = Join-Path $work 'err.txt'
    cmd /c "`"$LilyPond`" -dbackend=svg -o `"$work\o`" `"$probePath`" > `"$out`" 2> `"$err`" < NUL" | Out-Null

    $lines = @(Get-Content $out | Where-Object { $_ -match '^PROBEV ' })
    if (-not $lines) {
        throw "no PROBEV lines in $out — LilyPond's stderr was:`n  " +
              ((Get-Content $err -ErrorAction SilentlyContinue) -join "`n  ")
    }

    # A PROBEV line that does not parse is an ERROR, not a line to skip: a page whose system
    # list has a hole in it reports a gap between two systems that were never neighbours.
    $shapes = @(
        '^PROBEV BOOK (\S+)$'
        '^PROBEV PAPER top-margin=(\S+) bottom-margin=(\S+) paper-height=(\S+) paper-width=(\S+) output-scale=(\S+) line-width=(\S+)$'
        '^PROBEV PAGE (\d+) systems=(\d+)$'
        '^PROBEV SYS (\d+) (\d+) y=(\S+) ext=\((\S+) \. (\S+)\) staff=\((\S+) \. (\S+)\) title=(\d)$'
        '^PROBEV VAG (\d+) (\d+) rel=(\S+) aff=(\S+) ext=\((\S+) \. (\S+)\)$'
        '^PROBEV GROB (\d+) (\d+) name=(\S+) rel=(\S+) ext=\((\S+) \. (\S+)\) x=\((\S+) \. (\S+)\)$'
    )
    $bad = @($lines | Where-Object { $l = $_; -not ($shapes | Where-Object { $l -match $_ }) })
    if ($bad) {
        throw ("LilyPond printed {0} unparsable PROBEV line(s) — the dump is INCOMPLETE:`n  " -f $bad.Count) +
              ($bad -join "`n  ")
    }

    $books = [ordered]@{}
    $book = $null
    foreach ($line in $lines) {
        if ($line -match '^PROBEV BOOK (\S+)$') {
            $book = $Matches[1]
            $books[$book] = [pscustomobject]@{ Paper = $null; Systems = @(); Groups = @(); Grobs = @() }
        }
        elseif ($line -match '^PROBEV PAPER top-margin=(\S+) bottom-margin=(\S+) paper-height=(\S+) paper-width=(\S+) output-scale=(\S+) line-width=(\S+)$') {
            $books[$book].Paper = [pscustomobject]@{
                TopMargin    = [double]$Matches[1]
                BottomMargin = [double]$Matches[2]
                PaperHeight  = [double]$Matches[3]
                PaperWidth   = [double]$Matches[4]
                OutputScale  = [double]$Matches[5]
                LineWidth    = [double]$Matches[6]
            }
        }
        elseif ($line -match '^PROBEV VAG (\d+) (\d+) rel=(\S+) aff=(\S+) ext=\((\S+) \. (\S+)\)$') {
            # One per vertical axis group, spaceable or not. `aff` is staff-affinity: '()'
            # on a spaceable staff, 1/-1 on a loose line. The SYS line cannot carry these —
            # staff-refpoint-extent holds the SPACEABLE staves only, which is the set the
            # page's spring chain contains, and a loose line is by definition not in it.
            $books[$book].Groups += [pscustomobject]@{
                Page     = [int]$Matches[1]
                Index    = [int]$Matches[2]
                Rel      = [double]$Matches[3]   # relative to the system: negative is down
                Loose    = $Matches[4] -ne '()'
            }
        }
        elseif ($line -match '^PROBEV GROB (\d+) (\d+) name=(\S+) rel=(\S+) ext=\((\S+) \. (\S+)\) x=\((\S+) \. (\S+)\)$') {
            # An outside-staff grob riding above a staff. It is inside that staff's
            # VerticalAxisGroup skyline, so it sets the ink the system reserves above its own
            # reference point -- the term that closes a loose-line chain and floors the
            # system-to-system spring -- while appearing nowhere in the VAG or SYS lines.
            $books[$book].Grobs += [pscustomobject]@{
                Page  = [int]$Matches[1]
                Index = [int]$Matches[2]
                Name  = $Matches[3]
                Rel   = [double]$Matches[4]   # the grob's own refpoint (a text grob: its baseline)
                Down  = [double]$Matches[5]
                Up    = [double]$Matches[6]
                XL    = [double]$Matches[7]   # already about the SYSTEM, ready to intersect
                XR    = [double]$Matches[8]
            }
        }
        elseif ($line -match '^PROBEV SYS (\d+) (\d+) y=(\S+) ext=\((\S+) \. (\S+)\) staff=\((\S+) \. (\S+)\) title=(\d)$') {
            $books[$book].Systems += [pscustomobject]@{
                Page     = [int]$Matches[1]
                Index    = [int]$Matches[2]
                Y        = [double]$Matches[3]
                Down     = [double]$Matches[4]   # negative: ink extent below the refpoint
                Up       = [double]$Matches[5]
                StaffDown = [double]$Matches[6]
                StaffUp   = [double]$Matches[7]
                Title    = $Matches[8] -eq '1'
            }
        }
    }

    foreach ($tag in $books.Keys) {
        $b = $books[$tag]
        $p = $b.Paper
        Write-Host ""
        Write-Host ("=== book {0} ===" -f $tag) -ForegroundColor Cyan
        "  paper-height = {0:F6}   top-margin = {1:F6}   bottom-margin = {2:F6}" -f $p.PaperHeight, $p.TopMargin, $p.BottomMargin
        "  paper-width  = {0:F6}   line-width = {1:F6}   output-scale = {2:F6} mm/ss" -f $p.PaperWidth, $p.LineWidth, $p.OutputScale
        "  usable band  = {0:F6}   (paper-height - top-margin - bottom-margin)" -f ($p.PaperHeight - $p.TopMargin - $p.BottomMargin)

        foreach ($pageNo in ($b.Systems.Page | Select-Object -Unique)) {
            $sys = @($b.Systems | Where-Object Page -eq $pageNo | Sort-Object Index)
            # Distance from the TOP PAPER EDGE down to the system's own origin:
            # scm/page.scm:190 translates the system stencil by -(Y-offset + top-margin).
            $origin = $sys | ForEach-Object { $_.Y + $p.TopMargin }
            # ...and down to the STAFF refpoint, which is where LilyPond's vertical springs
            # actually attach. staff-refpoint-extent is the staves' refpoints measured from
            # the system origin, so it is NEGATIVE (the staff sits below the origin).
            #
            # THIS DISTINCTION IS THE WHOLE MEASUREMENT. The offset from origin to staff is
            # NOT the same on every system -- a system carrying a bar number above its staff
            # pushes its own origin further up than one without -- so origin-to-origin
            # distances differ system by system even when the spacing is uniform. Measured
            # that way the natural gap reads as 11.528583 on the first pair and 12.000000 on
            # the next; measured staff-to-staff both are exactly 12.000000, which is
            # system-system-spacing's basic-distance. The 11.528 that reached HANDOFF.md as
            # "the distance LilyPond compresses to" is that artefact, not a compression.
            $staff = for ($i = 0; $i -lt $sys.Count; $i++) { $origin[$i] - $sys[$i].StaffUp }
            $inkTop = $origin[0] - $sys[0].Up
            $inkBottom = $origin[-1] - $sys[-1].Down
            Write-Host ("  -- page {0}: {1} system(s) --" -f $pageNo, $sys.Count) -ForegroundColor DarkGray
            "     first ink top below paper edge   = {0:F6}   (top-margin {1:F6} + {2:F6})" -f $inkTop, $p.TopMargin, ($inkTop - $p.TopMargin)
            "     first STAFF refpoint below edge  = {0:F6}   (top-margin + {1:F6})" -f $staff[0], ($staff[0] - $p.TopMargin)
            "     last  ink bottom below edge      = {0:F6}   foot slack = {1:F6}" -f $inkBottom, ($p.PaperHeight - $inkBottom)
            # The refpoint extent is an INTERVAL over the system's spaceable staves
            # (lily/system.cc:705-717), so its width is the staff-to-staff distance inside
            # the system -- zero on a one-staff score, and on a two-staff one the number
            # Align_interface solved for. Printed only when it exists, so the single-staff
            # books stay as terse as they were.
            $insideAll = @(
                for ($i = 0; $i -lt $sys.Count; $i++) { $sys[$i].StaffUp - $sys[$i].StaffDown }
            )
            $inside = @($insideAll | Where-Object { $_ -gt 1e-9 })
            # THE BOTTOM OF THE CHAIN, which had no reading here at all. last-bottom-spacing
            # attaches to the LAST SPACEABLE STAFF's refpoint (page-layout-problem.cc:538-545),
            # not to the system origin and not to the ink, so this is the term that closes the
            # page's force. Without it, page.{stretched,compressed}.* are four readings of TWO
            # forces with nothing to attribute them to -- a force is slack over strength, and
            # the slack is not measurable from the gaps alone. Computed from the raw doubles
            # for the same reason the inter-system line below is.
            $lastStaff = $staff[-1] + $insideAll[-1]
            "     last  STAFF refpoint below edge  = {0:F6}   to foot = {1:F6}   ink below it = {2:F6}" -f `
                $lastStaff, ($p.PaperHeight - $lastStaff), ($inkBottom - $lastStaff)
            if ($inside) {
                $insideUniq = @($inside | ForEach-Object { "{0:F6}" -f $_ } | Select-Object -Unique)
                "     staff-to-staff INSIDE a system = {0}" -f ($insideUniq -join ', ')
            }
            # On a MULTI-STAFF score the gap printed below runs first-staff to first-staff, so
            # it carries the inside distance with it. The system-to-system spring is the part
            # that is left: LAST staff of one system to FIRST staff of the next. Computed here
            # from the raw doubles rather than left to the reader to subtract two F6 prints --
            # arithmetic on a ROUNDED gap is exactly how 3.544994 (for 3.550000) got into the
            # ledger's system.stretched-distance entry.
            if ($inside -and $sys.Count -ge 2) {
                $inter = for ($i = 1; $i -lt $sys.Count; $i++) {
                    $staff[$i] - ($staff[$i - 1] + $insideAll[$i - 1])
                }
                $interUniq = @($inter | ForEach-Object { "{0:F6}" -f $_ } | Select-Object -Unique)
                "     system-to-system (last staff -> next first) = {0}" -f ($interUniq -join ', ')
            }
            # LOOSE LINES (lyrics, chord rows, dynamics): distance from the staff refpoint
            # ABOVE them, per system. Printed only when the score has any, so every existing
            # book's output is unchanged. This is the quantity distribute_loose_lines
            # decides, and the reason it is worth printing next to the gaps is that it does
            # NOT follow them: get_spacing_spec gives a loose line's springs to its NON-own
            # side LARGE_STRETCH/HUGE_STRETCH (page-layout-problem.cc:1257-1338), so the row
            # stays with its own staff while the page around it stretches.
            #
            # CONSECUTIVE distances down the chain, not all-of-them-from-the-staff: with two
            # loose lines (a second verse) the quantity in question is the step BETWEEN them,
            # which LilyPond takes from a different spec entirely (nonstaff-nonstaff-spacing,
            # via get_spacing_spec's loose-loose branch :1327-1332). Computed from the raw
            # doubles for the same reason the inter-system line above is: subtracting two F6
            # prints is how 3.544994 (for 3.550000) got into the ledger once.
            $loose = @(
                foreach ($sysNo in ($b.Groups | Where-Object Page -eq $pageNo |
                                    Select-Object -ExpandProperty Index -Unique)) {
                    $vags = @($b.Groups | Where-Object { $_.Page -eq $pageNo -and $_.Index -eq $sysNo } |
                              Sort-Object Rel -Descending)
                    $anchor = $null
                    foreach ($v in $vags) {
                        if (-not $v.Loose) { $anchor = $v.Rel }
                        elseif ($null -ne $anchor) { $anchor - $v.Rel; $anchor = $v.Rel }
                    }
                }
            )
            if ($loose) {
                $looseUniq = @($loose | ForEach-Object { "{0:F6}" -f $_ } | Select-Object -Unique)
                "     staff/loose -> next loose line = {0}" -f ($looseUniq -join ', ')
            }
            # OUTSIDE-STAFF grobs (today: BarNumber), measured from the staff refpoint they
            # ride over. Printed only when the book has any. This is the other half of what
            # decides `min_offsets[0]`, and it is X-AWARE on LilyPond's side: a bar number
            # stands left of the clef, so a high melody -- which starts after it -- cannot
            # push it up. That invariance is what books BNL/BNH are for.
            $outside = @(
                foreach ($g in ($b.Grobs | Where-Object Page -eq $pageNo | Sort-Object Index)) {
                    $s = $sys | Where-Object Index -eq $g.Index
                    if ($s) {
                        [pscustomobject]@{
                            Name = $g.Name; Above = $g.Rel - $s.StaffUp; XL = $g.XL; XR = $g.XR
                        }
                    }
                }
            )
            if ($outside) {
                foreach ($name in ($outside.Name | Select-Object -Unique)) {
                    $vals = @($outside | Where-Object Name -eq $name |
                              ForEach-Object { "{0:F6}" -f $_.Above } | Select-Object -Unique)
                    "     staff refpoint -> {0} baseline = {1}" -f $name, ($vals -join ', ')
                    $xs = @($outside | Where-Object Name -eq $name |
                            ForEach-Object { "[{0:F6}, {1:F6}]" -f $_.XL, $_.XR } | Select-Object -Unique)
                    "                     {0} X span     = {1}" -f $name, ($xs -join ', ')
                }
            }
            if ($sys.Count -ge 2) {
                $gaps = for ($i = 1; $i -lt $sys.Count; $i++) { $staff[$i] - $staff[$i - 1] }
                $uniq = @($gaps | ForEach-Object { "{0:F6}" -f $_ } | Select-Object -Unique)
                if ($uniq.Count -eq 1) {
                    "     staff-to-staff gap               = {0} (all {1})" -f $uniq[0], @($gaps).Count
                }
                else {
                    "     staff-to-staff gaps              = {0}" -f ($uniq -join ', ')
                }
            }
        }
    }

    Write-Host ""
    Write-Host "Paste the relevant figures into audit/lp-geometry/lp-geometry.json ('lilypond')." -ForegroundColor Yellow
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
