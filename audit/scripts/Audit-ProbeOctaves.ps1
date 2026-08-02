<#
Probe octave audit — does every Lily# probe book spell the SAME MUSIC as its .ly twin?

WHY THIS EXISTS. Lily#'s absolute `c` is staff position -6 = C4, so Lily# `c` IS LilyPond
`c'`: a probe whose Lily# side was written with LilyPond's spelling is a whole octave out and
the two engines are not being asked the same question. That has bitten three times, most
recently flag.up.reach (session 71), where a -1.613200 residual was an octave and not a
defect. The standing rule is GENERATE the twin, never write it — this script checks the rule
was followed, for every entry in the ledger at once.

HOW. Each Lily# probe source is run through `lysc ly`, and BOTH sides are reduced to a
sequence of ABSOLUTE pitch tokens: \fixed <ref> shifts the tokens inside it, a pure-octave
\transpose shifts the whole file, and comparison is "does the generated sequence appear in
the .ly's". Anything the reduction cannot read is FLAGGED, never silently passed.

⚠️ TWO WAYS THIS SCRIPT HAS ALREADY BEEN WRONG, both caught by opening a file it accused:
  1. It returned List[string] through `return ,$x`; both sides joined to the literal text
     "System.Collections.Generic.List`1[System.String]" and all 232 books "matched". A
     check that cannot fail proves nothing — hence the SELF-CHECK at the bottom, which
     re-injects the real session-71 defect and requires the script to catch it.
  2. It applied \fixed on the GENERATED side only. Three probe .ly files are themselves
     `lysc ly` output and carry \fixed c' too (beam-voice-span-scope.ly says so in its own
     header), so they read as a whole octave out when they agree perfectly. ONE reduction is
     now used for both sides, which is the only way the two can be compared at all.

USAGE
  # 1. dump the Lily# sources (a scratch xunit test writes probe.Id + ".lys" per entry)
  # 2. lysc ly each distinct one into <Dir>\gen\<id>.ly
  # 3. .\Audit-ProbeOctaves.ps1 -Dir <Dir>
#>
param(
    [string]$Dir    = "$env:TEMP\lys-probe-sources",
    [string]$Probes = 'C:\MyProj\LilySharp\audit\lp-geometry\probes',
    [string]$Ledger = 'C:\MyProj\LilySharp\audit\lp-geometry\lp-geometry.json',
    [switch]$Quiet
)

# ⚠️ NOT $P. PowerShell variable names are CASE-INSENSITIVE and its scoping is DYNAMIC, so a
# `foreach ($p in …)` loop inside a function silently rebinds a script-scope $P for every
# function that function calls. That is a third way this script has been wrong: Strip's
# \key removal turned into `\key\s+\s*\\\w+`, which matches nothing, and every generated book
# grew a leading `c` from `\key c \major` — 49 books "mismatched" while the same helpers,
# called from script scope, answered MATCH.
$PARG = "[a-g](?:is|es)*['" + ',' + "]*"
$PITCH = "(?<![\\A-Za-z])(?<n>[a-g](?:is|es)+|[a-g])(?<o>['" + ',' + "]*)(?![a-zA-Z])"

function Marks([string]$o) {
    $n = 0
    foreach ($ch in $o.ToCharArray()) { if ($ch -eq "'") { $n++ } else { $n-- } }
    $n
}

function Strip([string]$t) {
    $t = $t -replace '%\{[\s\S]*?%\}', ' '
    $t = ($t -split "`n" | ForEach-Object { $_ -replace '%.*$', '' }) -join "`n"
    $t = $t -replace '"[^"]*"', ' '
    # Commands that TAKE a pitch must go before the generic \word strip or their argument
    # survives as a note. `\key c \major` was the whole of one run's 168 false mismatches.
    $t = $t -replace "\\key\s+$PARG\s*\\\w+", ' '
    $t = $t -replace "\\transposition\s+$PARG", ' '
    $t = $t -replace "\\transpose\s+$PARG\s+$PARG", ' '
    $t = $t -replace "\\fixed\s+$PARG", ' '
    $t = $t -replace '\\[a-zA-Z]+', ' '
    $t = $t -replace '#\S+', ' '
    $t
}

function PitchString([string]$t, [int]$shift) {
    $sb = [System.Text.StringBuilder]::new()
    foreach ($m in [regex]::Matches($t, $PITCH)) {
        $net = $shift + (Marks $m.Groups['o'].Value)
        $mark = if ($net -gt 0) { "'" * $net } elseif ($net -lt 0) { ',' * (-$net) } else { '' }
        if ($sb.Length -gt 0) { [void]$sb.Append(' ') }
        [void]$sb.Append($m.Groups['n'].Value).Append($mark)
    }
    $sb.ToString()
}

# ONE reduction, used for BOTH sides — that symmetry is the point, and getting it wrong is
# how this script accused a correct book (see the header). \fixed segments the file and
# shifts the tokens inside it.
#
# ⚠️ \transpose IS DELIBERATELY IGNORED, on both sides. Both files are `lysc ly`-shaped —
# a music VARIABLE plus a \score wrapper — and a transposing instrument's \transpose c c,
# sits in the WRAPPER, applying to that score only. Shifting a whole multi-score .ly by it
# was wrong; leaving it out compares the variable bodies, which is the thing being audited.
function AbsolutePitchString([string]$raw) {
    $marks = [regex]::Matches($raw, "\\fixed\s+[a-g](?:is|es)*(?<o>['" + ',' + "]*)")
    if ($marks.Count -eq 0) { return (PitchString (Strip $raw) 0) }
    $parts = @()
    for ($i = 0; $i -lt $marks.Count; $i++) {
        $start = $marks[$i].Index + $marks[$i].Length
        $end = if ($i + 1 -lt $marks.Count) { $marks[$i + 1].Index } else { $raw.Length }
        $s = PitchString (Strip $raw.Substring($start, $end - $start)) (Marks $marks[$i].Groups['o'].Value)
        if ($s) { $parts += $s }
    }
    ($parts -join ' ')
}

function Audit([string]$SrcDir) {
    $j = Get-Content $Ledger -Raw | ConvertFrom-Json
    $meta = @{}
    foreach ($prop in $j.entries.PSObject.Properties) { $meta[$prop.Name] = $prop.Value }
    $lyCache = @{}
    $rows = @()
    foreach ($g in (Get-ChildItem $SrcDir -Filter '*.lys' |
                    Group-Object { (Get-FileHash $_.FullName -Algorithm MD5).Hash })) {
        $rep = $g.Group[0]
        $ids = @($g.Group | ForEach-Object { $_.BaseName })
        $gen = Join-Path $SrcDir "gen\$($rep.BaseName).ly"
        if (-not (Test-Path $gen)) { continue }
        $genRaw = [IO.File]::ReadAllText($gen)
        $needle = AbsolutePitchString $genRaw
        $tokens = if ($needle) { @($needle -split ' ').Count } else { 0 }
        $probeFiles = @($ids | ForEach-Object { $meta[$_].probe } | Sort-Object -Unique)

        $verdict = 'MISMATCH'
        if ($genRaw -match '\\relative') { $verdict = 'GEN-RELATIVE' }
        elseif ($tokens -eq 0) { $verdict = 'NO-PITCHES' }
        elseif ($tokens -lt 2) { $verdict = 'SHORT' }
        else {
            foreach ($pf in $probeFiles) {
                $path = Join-Path $Probes $pf
                if (-not (Test-Path $path)) { $verdict = 'NO-PROBE-FILE'; break }
                if (-not $lyCache.ContainsKey($pf)) {
                    $raw = [IO.File]::ReadAllText($path)
                    $flag = ''
                    if ($raw -match '\\relative') { $flag = 'LY-RELATIVE' }
                    elseif ($raw -match '\\lyricmode|\\addlyrics|\\lyricsto') { $flag = 'LY-LYRICS' }
                    $lyCache[$pf] = @{ Flag = $flag; Text = (AbsolutePitchString $raw) }
                }
                if ($lyCache[$pf].Flag) { $verdict = $lyCache[$pf].Flag; continue }
                if ($lyCache[$pf].Text.Contains($needle)) { $verdict = 'MATCH'; break }
                # A mismatch whose PITCH SET agrees is a repetition artifact, not an octave:
                # the .ly writes a run once under \repeat unfold or a variable and the Lily#
                # book writes it out. Only a token the .ly does not contain at all is a lead.
                $mine = @($needle -split ' ' | Sort-Object -Unique)
                $theirs = @($lyCache[$pf].Text -split ' ' | Sort-Object -Unique)
                $verdict = if (@($mine | Where-Object { $theirs -notcontains $_ }).Count -eq 0)
                           { 'SAME-PITCH-SET' } else { 'MISMATCH' }
            }
        }
        $rows += [pscustomobject]@{
            Verdict = $verdict; Probe = ($probeFiles -join ','); Entries = $ids.Count
            FirstId = $ids[0]; Tokens = $tokens; Mine = $needle
        }
    }
    , $rows
}

$rows = Audit $Dir
if (-not $Quiet) {
    $rows | Group-Object Verdict | Sort-Object Name |
        ForEach-Object { "{0,-16} {1,4} books  {2,4} ledger entries" -f $_.Name, $_.Count,
                         (($_.Group | Measure-Object Entries -Sum).Sum) }
    "--- MISMATCH (the only verdict that is a lead) ---"
    $rows | Where-Object { $_.Verdict -eq 'MISMATCH' } | Sort-Object Probe |
        Format-Table Probe, FirstId, Tokens, Mine -AutoSize -Wrap
    $rows | Export-Csv (Join-Path $Dir 'audit.csv') -NoTypeInformation -Encoding UTF8
}

# --- SELF-CHECK: the audit must be able to FAIL, or its passes mean nothing. ---
$fx = Join-Path $Dir '_falsify'
if (Test-Path (Join-Path $fx 'gen')) {
    $v = @((Audit $fx).Verdict)
    if ($v -contains 'MISMATCH') { "SELF-CHECK ok: the re-injected session-71 octave reads $v" }
    else { "SELF-CHECK FAILED: the known-bad book reads $v -- this audit proves nothing" }
} else {
    "SELF-CHECK SKIPPED: no _falsify\gen under $Dir"
}
