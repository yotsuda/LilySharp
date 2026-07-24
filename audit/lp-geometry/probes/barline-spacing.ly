\version "2.26.0"
%% LP FIDELITY PROBE — bar-line spacing (Staff_spacing::get_spacing and its neighbours).
%%
%% Produces the numbers in ../lp-geometry.json under the "barline.*" keys. Run it with
%% ../Measure-LilyPondGeometry.ps1, which prints one line per score ready to paste.
%%
%% Each score below has a twin in LilySharp.Tests/LpFidelity/LpGeometryProbes.cs engraving
%% the SAME music. Mind the octave convention: Lily# `c` is LilyPond `c'`. The twin probes
%% name their counterpart in a comment; keep both sides in step or the comparison is
%% meaningless while still looking green.
%%
%% Every score is TWO measures and one system, so "the bar line" is unambiguous.
%%
%% ragged-right is deliberate: it puts every spring at force 0, i.e. at its natural length,
%% so what is measured is the spring's ideal rather than a share of some line's stretch.
%% (Stretch strength is verified separately by justifying the same music — see
%% SpacingInvariantTests.BarlineToFirstNoteSpring_StretchesByHalfTheSpaceAlistDistance.)

\header { tagline = ##f }

%% These dumps go to STDOUT. Keep LilyPond's stderr on its own stream when running this
%% (Measure-LilyPondGeometry.ps1 does): merging the two splices LilyPond's own diagnostics
%% into the MIDDLE of a dump line. Under -dbackend=null it always reports "Unbound
%% variable: output-stencils" at book-handling time, and that once landed inside score MC's
%% third note head, leaving a truncated `PROBE MC HEAD x=` that the parser then discarded —
%% so the probe looked complete while `clef -> next note` was actually being measured to the
%% FOURTH head. The value was never missing; the line was cut in half.

#(define ((gd tag name) g)
   (format #t "\nPROBE ~a ~a x=~a ext=~a\n" tag name
           (ly:grob-relative-coordinate g (ly:grob-system g) X)
           (ly:grob-extent g g X)))

lay =
#(define-scheme-function (tag) (string?)
   #{
     \layout {
       ragged-right = ##t
       line-width = 500\mm
       indent = 0
       \context {
         \Score
         \override BarLine.after-line-breaking         = #(gd tag "BAR")
         \override NoteHead.after-line-breaking        = #(gd tag "HEAD")
         \override Clef.after-line-breaking            = #(gd tag "CLEF")
         \override Rest.after-line-breaking            = #(gd tag "REST")
         \override Accidental.after-line-breaking      = #(gd tag "ACC")
         \override KeySignature.after-line-breaking    = #(gd tag "KEY")
         %% A change to a key with FEWER accidentals engraves the naturals as a separate
         %% KeyCancellation grob, and leaves the KeySignature itself empty. Dumping only
         %% KeySignature would measure to a grob with no ink (extent +inf.0 . -inf.0) while
         %% the glyphs the eye sees belong to the cancellation. Both are dumped; the empty
         %% one is dropped by the script, which says so.
         \override KeyCancellation.after-line-breaking = #(gd tag "KEY")
         \override TimeSignature.after-line-breaking   = #(gd tag "TIME")
       }
     }
   #})

%% A — plain measure start, UP stems after the bar line.
\score { \new Staff { \time 4/4 c'4 d' e' f' | g'4 a' b' c'' } \lay "A" }

%% NN — plain NOTE-TO-NOTE spacing, which this corpus has never measured. Every existing
%%   point measures FROM something: barline.next.* from a bar line, line-start.* from the
%%   prefix. Nothing measures one note to the next, so Lily#'s duration space has only ever
%%   been checked through those. Ragged, so this reads the spring's IDEAL and is
%%   paper-independent like its neighbours.
%%   Mixed durations on ONE pitch: the quarter gap and the eighth gap are the pair. One
%%   pitch keeps the columns' skylines a plain reach difference and earns no stem-direction
%%   correction, so what is left is the duration space alone. The eighths are beamed, so no
%%   flag widens a left column.
\score { \new Staff { \time 4/4 c'4 c'8 c' c'4 c'8 c' | c'4 c'8 c' c'4 c'8 c' } \lay "NN" }

%% HR — the same note-to-note question in the regime NN cannot reach: a score whose
%%   shortest note is a QUARTER, with a HALF note gap and a REST as a spacing target.
%%   Three holes at once, and all three were found by decomposing a real fixture
%%   (test/ties-slurs) column by column against LilyPond -- see
%%   probes/ties-slurs-breaks-ragged.ly.
%%
%%   WHY THE SHORTEST MATTERS. Spacing_spanner::find_shortest does not use the score's
%%   shortest note: it takes the most common per-measure shortest and AVERAGES it with
%%   base-shortest-duration (1/8). In NN, where the most common shortest IS 1/8, the
%%   average is 1/8 again and the averaging is INVISIBLE -- so the whole corpus has only
%%   ever measured the case where it does nothing. Here the most common shortest is 1/4,
%%   so global_shortest is (1/4 + 1/8)/2 = 3/16 and a quarter's ratio is 4/3 rather than
%%   2: predicted (2 + log2 4/3) * 1.2 - 1.2 + head 1.304200 = 3.002245, against NN's
%%   3.704200 for the same quarter. Measured on the ties-slurs twin: 3.002245.
%%
%%   The half gap is the second reading, and it is NOT the quarter's plus the increment
%%   1.2: a HALF notehead is wider than a black one, and Note_spacing adds the LEFT
%%   head's width. Predicted 3.002245 + 1.2 + (half head - black head) and measured on
%%   the twin as 4.275445, i.e. that difference is 0.073200 -- so this point reads a
%%   glyph metric no other note-to-note point does.
%%
%%   The third is the REST as the RIGHT column: every existing rest point (F, and
%%   barline.prev.whole-rest) measures a rest against a BAR LINE, never a note against a
%%   rest. Predicted equal to the quarter gap, since the space is the LEFT column's
%%   duration and head; the twin measured 3.002245, which is that prediction.
%%
%%   Bar 3 exists so the most common per-measure shortest is unambiguously the quarter
%%   (two measures against one), and so that nothing read here touches the FINAL bar
%%   line, whose column is placed by its ink RIGHT edge (ext -0.19 .. 0) rather than its
%%   left the way an interior one is.
\score { \new Staff { \time 4/4 c'2 c'2 | c'4 c'4 r2 | c'4 c'4 c'4 c'4 } \lay "HR" }

%% B — clef change AT the bar line, DOWN stems after it.
\score { \new Staff { \time 4/4 c'4 d' e' f' \clef bass g4 a b c' } \lay "B" }

%% C — no clef, DOWN stems. Together with D this is the 2x2 that proves
%%     next_notes_correction tracks the STEM and not the clef.
\score { \new Staff { \time 4/4 c'4 d' e' f' | a''4 b'' c''' d''' } \lay "C" }

%% D — clef change AT the bar line, UP stems after it. Earns no correction at all.
\score { \new Staff { \time 4/4 c'4 d' e' f' \clef bass c,4 d, e, f, } \lay "D" }

%% E — a single whole note fills the measure, so full-measure-extra-space applies.
\score { \new Staff { \time 4/4 c'1 | c'1 } \lay "E" }

%% F — whole rests.
\score { \new Staff { \time 4/4 r1 | r1 } \lay "F" }

%% G — half notes.
\score { \new Staff { \time 4/4 c'2 c'2 | c'2 c'2 } \lay "G" }

%% LSCT / LSCB — line-start CLEF-ONLY prefix (the meter glyph omitted), the only prefix where
%%   Clef's (first-note . minimum-fixed-space . 5.0) binds the first note. The ledger quantity
%%   is the CLEF anchor -> first HEAD anchor. staff-spacing.cc:183-187 puts the head at
%%   last_ext[LEFT] + max(last_ext.length(), distance) = clef-left + max(clef-width, 5.0), and
%%   since every engraved clef is under 5 ss wide the clef width is ABSORBED by the max rather
%%   than added: a treble clef (ink 2.565) and the WIDER bass clef (2.683) both put the head at
%%   0.8 + 5.0 = 5.8 for a clef-to-head distance of 5.0 EXACTLY. That identity is the pair's
%%   cross-check -- a defect that ADDS the clef width makes the two disagree, while a wrong
%%   fixed constant keeps them equal. The Lily# twin (LpGeometryProbes LSCT/LSCB) reaches the
%%   same clef-only prefix on an INTERIOR system, since Lily# cannot omit the meter on system 1;
%%   this omit-time single system was measured equal to that interior system (clef@0.8, head@5.8
%%   either way), the documented harness asymmetry the octave spelling already carries.
\score { \new Staff { \omit Staff.TimeSignature \time 4/4 c'1 c'1 } \lay "LSCT" }
\score { \new Staff { \omit Staff.TimeSignature \clef bass \time 4/4 c1 c1 } \lay "LSCB" }

%% DCT / DCB — defect-3: SpacingRules.CalculatePrefixWidth reserves a FIXED GClefWidth for
%%   EVERY clef, so a wider clef's meter (and, on a metered first system, its first note) is
%%   placed as if the clef were a treble G. The line-start meter binds through Clef.space-alist
%%   (time-signature . (extra-space . 1.52)), measured off the clef's OWN ink RIGHT edge
%%   (last_ext[RIGHT]), so the CLEF anchor -> TIME anchor distance rides on the clef ink width.
%%   Treble is the CONTROL (GClefWidth == the G clef's own ink 2.565, so DCT reads ~0), bass is
%%   the DEFECT (F clef ink 2.683 vs GClefWidth 2.565). LilyPond spaces the meter off the ACTUAL
%%   clef ink, so DCB's clef->time is 0.118 WIDER than DCT's; Lily#, reserving GClefWidth for
%%   both, prints them EQUAL -- the pair's cross-check. The Lily# twin measures the same clef
%%   anchor -> time-signature anchor on the first (metered) system.
\score { \new Staff { \time 4/4 c'1 c'1 } \lay "DCT" }
\score { \new Staff { \clef bass \time 4/4 c1 c1 } \lay "DCB" }

%% DCP — clef -> time with a PERCUSSION clef, the last of defect-3. Unlike the pitched clefs,
%%   the percussion glyph's ink does NOT start at the grob origin: rendered on 2.26.0 the CLEF
%%   grob sits at x=0.13 with ext (0.67 . 2.0), so its ink-left is 0.13+0.67 = 0.8 (the same
%%   LeftEdge->clef 0.8 as every clef) and its ink-right is 0.13+2.0 = 2.13. The meter binds
%%   1.52 off that ink right edge -> TIME at 3.65, so the CLEF anchor -> TIME anchor distance
%%   is 3.65-0.13 = 3.52. Lily# reserved GClefWidth (2.565) for the percussion clef AND drew
%%   the glyph at the origin without the 0.67 ink-left offset, so its twin read 4.085 (the
%%   treble value) until both were fixed.
\score { \new Staff { \clef percussion \time 4/4 c'1 c'1 } \lay "DCP" }

%% TSA — cross-staff time-signature alignment. A grand staff whose staves carry DIFFERENT key
%%   signatures (upper D major = 2 sharps, lower C major = none), like a transposed part beside
%%   a concert one. Break-alignment shares the KeySignature/TimeSignature columns across staves
%%   (break-alignment-interface.cc:141-142,242 — the KeySignature group extent is the union), so
%%   BOTH TimeSignatures print at the SAME x, past the WIDEST key: the lower staff's meter is NOT
%%   tight against its clef but aligned under the upper's. The twin dumps TWO TIME grobs; their x
%%   are EQUAL (spread 0). The Lily# twin measures max-min of the per-staff meter x.
\score { \new PianoStaff << \new Staff { \key d \major \time 4/4 d'1 d'1 }
                            \new Staff { \clef bass \key c \major \time 4/4 c1 c1 } >> \lay "TSA" }

%% DCTK — clef -> time on a KEYED staff (D major, 2 sharps). The meter binds through the key,
%%   not the clef: KeySignature.space-alist (time-signature . (extra-space . 1.15)) measured
%%   off the KEY's ink RIGHT edge -- NO extra pad. Rendered: CLEF 0.8, KEY 4.185 (ext 2.2),
%%   TIME 7.535 (= 4.185 + 2.2 + 1.15), so clef->time = 6.735. The Lily# twin once drew the
%%   meter a KeySigTrailingGap 0.4 further right (draw-vs-reserve split); this probe guards
%%   the unified, key-ink-measured column.
\score { \new Staff { \key d \major \time 4/4 d'1 d'1 } \lay "DCTK" }

%% X — an accidental opens the second measure. Its leftmost ink is the accidental, which
%%     declares extra-spacing-width (-0.2 . 0.0) rather than the default 0.1.
\score { \new Staff { \time 4/4 c'4 d' e' f' | cis'4 d' e' f' } \lay "X" }

%% NAT — the single-note accidental DRAW gap: a natural (c-natural in D major cancels the key's
%%     C#) sits before its head at the distance its REAL right skyline clears the head, NOT a
%%     fixed AccidentalNoteGap. Rendered on 2.26.0: HEAD anchor 11.569272 (ext 0 . 1.962),
%%     natural ACC anchor 10.535000 (ext 0 . 0.6666), so ink gap = 11.569272 - (10.535 + 0.6666)
%%     = 0.367672 (NOT 0.35) and HEAD - ACC anchor = 1.034272. A sharp/flat control clears at
%%     exactly 0.35 (box), so only the natural's skyline term (0.017672) shows.
\score { \new Staff { \key d \major c'1 } \lay "NAT" }

%% FLAT — the flat's ink starts 0.12 LEFT of its origin (LILC bbox left -0.12), so a single-note
%%     draw that seats the glyph at `head - width - gap` over-counts the overhang and places the
%%     flat at gap 0.47, not LilyPond's 0.35. Rendered on 2.26.0: HEAD anchor 9.155 (ext 0 . 1.962),
%%     flat ACC anchor 8.005 (ext -0.12 . 0.8), ink gap 9.155 - (8.005 + 0.8) = 0.35, HEAD - ACC = 1.15.
\score { \new Staff { \key c \major ces'1 } \lay "FLAT" }

%% CSB/CSA, CFB/CFA — the first probes to reach Accidental_placement's CHORD stacking
%%     (calc_positioning_done -> position_apes). Score X above measures a SINGLE accidental;
%%     these carry a cluster whose two accidentals' glyphs OVERLAP vertically and so are
%%     forced into TWO columns. Both accidentals are dumped as ACC grobs, so the raw per-grob
%%     anchors printed by Measure-LilyPondGeometry.ps1 give the ACC-to-ACC COLUMN GAP — the
%%     quantity that measures the stacking itself. That gap is measured between two accidentals
%%     of the SAME glyph, so whatever left-bearing the glyph's anchor carries cancels in the
%%     difference (the flat's anchor sits 0.12 right of its ink; the sharp's coincides).
%%
%%     THE PAIRS ARE VERTICAL MIRRORS, the P/Q discipline: the accidentals are always placed
%%     to the LEFT of the note column, right-to-left against the heads' LEFT skyline, and the
%%     stems never protrude past the head boxes on that side, so the column gap is independent
%%     of stem direction. A chord below the middle line (stems up) and its exact reflection
%%     above it (stems down) must therefore print the SAME gap; a difference is a
%%     direction-dependence defect on its own, whatever the value.
%%
%%     A THIRD (two staff positions) is used, not a second: a third does NOT reverse a head
%%     across the stem, so the heads share one column and the heads skyline is clean, while an
%%     accidental glyph (~2.6 ss tall) still overlaps its neighbour 1 ss away and must stack.
%%     Trailing notes are a' b' c'' (letters A/B/C) so none of the four chords' altered letters
%%     (D/F, E/G) recurs in the bar and no cancellation natural is engraved as a third ACC.
%%
%% CSB — two SHARPS a third apart BELOW the middle line (dis' -5, fis' -3), stems up.
\score { \new Staff { \time 4/4 c'4 d' e' f' | <dis' fis'>4 a' b' c'' } \lay "CSB" }

%% CSA — the mirror ABOVE the middle line (eis'' +3, gis'' +5), stems down. Must equal CSB.
\score { \new Staff { \time 4/4 c'4 d' e' f' | <eis'' gis''>4 a' b' c'' } \lay "CSA" }

%% CFB — two FLATS a third apart BELOW the middle line (ees' -4, ges' -2), stems up. Flats take
%%     the merge-overlap that sharps do not — the site of Lily#'s invented 0.375 constant, which
%%     accidental-placement.cc does NOT contain (LP's flats interlock via their real glyph
%%     skylines). This pair is where that invention is measured against LP.
\score { \new Staff { \time 4/4 c'4 d' e' f' | <ees' ges'>4 a' b' c'' } \lay "CFB" }

%% CFA — the mirror ABOVE the middle line (des'' +2, fes'' +4), stems down. Must equal CFB.
\score { \new Staff { \time 4/4 c'4 d' e' f' | <des'' fes''>4 a' b' c'' } \lay "CFA" }

%% K — mid-line key change (break-aligned into the boundary column by LilyPond).
\score { \new Staff { \time 4/4 c'4 d' e' f' \key a \major c'4 d' e' f' } \lay "K" }

%% T — mid-line time change (likewise break-aligned).
\score { \new Staff { \time 4/4 c'4 d' e' f' \time 3/4 c'4 d' e' } \lay "T" }

%% --- MID-MEASURE changes -------------------------------------------------------------
%% These sit INSIDE a measure rather than at a bar line, so they are not break-aligned at
%% all: LilyPond gives the change its own musical column between two notes. This is the
%% case COORDINATE_AUDIT.md 4.7 item 1 governs (the change-item branch of the extent
%% helpers), and it is measured NOTE-to-GLYPH rather than from a bar line.

%% MC — mid-measure clef change. The common case by far.
\score { \new Staff { \time 4/4 c'4 d' \clef bass e4 f4 } \lay "MC" }

%% MK — mid-measure key change.
\score { \new Staff { \time 4/4 c'4 d' \key a \major e'4 f'4 } \lay "MK" }

%% MKA — mid-measure key change whose FOLLOWING note carries an accidental. The accidental
%%       is the musical column's leftmost ink, so it enters Paper_column::minimum_distance
%%       and the staff-spacing.cc:213 correction lifts the gap ABOVE the space-alist ideal.
%%       MK cannot see that branch: its next note is a bare head, so the ideal wins there.
\score { \new Staff { \key g \major \time 4/4 c'4 d' \key c \major fis'4 g'4 } \lay "MKA" }

%% NO mid-measure TIME probe. `\time 3/4` inside a 4/4 bar makes LilyPond restructure the
%% measures rather than engrave a change column, and the resulting dump is not the thing we
%% would be comparing against. An uninterpretable probe is worse than no probe: it would
%% look like coverage. If a mid-measure time change ever needs a number, engrave it with
%% \partial / explicit bar checks so both sides agree on where the bars are.

%% --- LINE-START, the reserve/draw split regimes (the break-align draw walk) -----------

%% KCS / KCC — the SAME two sharps, once as a standard D major and once as a
%%   non-traditional signature (keyAlterations set directly; Key_engraver prints whenever
%%   lastKeyAlterations != keyAlterations, key-engraver.cc:148-151). LilyPond has only ONE
%%   key model — keyAlterations — so the two scores dump byte-identical geometry (verified:
%%   every grob equal to 15 digits). The ledger quantity is TIME anchor -> first HEAD
%%   anchor: head = time ink-right + 2.0 (TimeSignature.space-alist (first-note .
%%   (semi-shrink-space . 2.0)), at its natural length under ragged-right) = 9.235 + 2.0,
%%   giving 3.700000 from the TIME anchor. Lily#'s reservation reads KeySignature.Sharps
%%   ONLY (a custom key is KeySignature(0, custom)), so KCS is the CONTROL and KCC the
%%   defect: the pair's disagreement on the Lily# side isolates the missing key column.
\score { \new Staff { \key d \major \time 4/4 d'4 e' fis' g' | a'4 b' cis'' d'' } \lay "KCS" }
\score { \new Staff {
  \set Staff.keyAlterations = #`((3 . ,SHARP) (0 . ,SHARP))
  \time 4/4 d'4 e' fis' g' | a'4 b' cis'' d''
} \lay "KCC" }

%% KC2 — the cut-common half of the C-glyph width pair: LilyPond's DEFAULT style prints
%%   2/2 as the timesig.C22 GLYPH (make-c-time-signature-markup,
%%   time-signature-settings.scm:954-964 — only 2/2 and 4/4 take the glyph path; every
%%   other fraction is \number markup / Pango, the path the digit ledger points pin).
%%   C22's LILC ink is 1.7, the same as C44, so the ledger quantity (TIME anchor -> first
%%   HEAD anchor = ink 1.7 + semi-shrink-space 2.0) must equal KCS's 3.700000 exactly —
%%   the pair's cross-check. Lily# reserved BOTH from the digit Pango table
%%   (GetTimeSigWidth), the wrong path for the C glyphs it draws.
\score { \new Staff { \key d \major \time 2/2 d'2 e' | fis'2 g' } \lay "KC2" }

%% OKN / OKNF — an ossia (NR "Ossia staves" recipe: fontSize -3 + staff-space magstep -3,
%%   firstClef = ##f, no Time_signature_engraver) above a keyed main staff. The ossia has
%%   NO clef, yet LilyPond break-aligns its KeySignature into the ONE key column spanning
%%   the whole system (break-alignment-interface.cc:141-142 — the group extent is the
%%   union across staves): OKEY x == the main staff's KEY x (4.185) exactly, so the ledger
%%   quantity — ossia KEY anchor minus main KEY anchor — is 0, metric-free (two anchors in
%%   one render). The prediction written before this dump said "LeftEdge -> key-signature
%%   extra-space 0.8" (define-grobs.scm:2097); the dump refuted it — column sharing wins,
%%   for the NR recipe, for \magnifyStaff (OKM below), for sharps and for flats. The pair
%%   (sharps / flats) must print the same 0; content-dependence is its cross-check.
%%   Lily# twin asymmetry, documented: the LP ossia staff spans both measures (R1 prints a
%%   whole-measure rest), while the Lily# ossia is a measure-1 fragment — the quantity is
%%   at the line start, before the difference can matter.
\score { <<
  \new Staff = "main" { \key d \major \time 4/4 d'4 e' fis' g' | a'4 b' cis'' d'' }
  \new Staff \with {
    alignAboveContext = "main"
    fontSize = #-3
    \override StaffSymbol.staff-space = #(magstep -3)
    firstClef = ##f
    \remove "Time_signature_engraver"
    \override KeySignature.after-line-breaking = #(gd "OKN" "OKEY")
    \override NoteHead.after-line-breaking = #(gd "OKN" "OHEAD")
  } { \key d \major d''4 e'' fis'' g'' | R1 }
>> \lay "OKN" }
\score { <<
  \new Staff = "main" { \key bes \major \time 4/4 d'4 ees' f' g' | a'4 bes' c'' d'' }
  \new Staff \with {
    alignAboveContext = "main"
    fontSize = #-3
    \override StaffSymbol.staff-space = #(magstep -3)
    firstClef = ##f
    \remove "Time_signature_engraver"
    \override KeySignature.after-line-breaking = #(gd "OKNF" "OKEY")
    \override NoteHead.after-line-breaking = #(gd "OKNF" "OHEAD")
  } { \key bes \major d''4 ees'' f'' g'' | R1 }
>> \lay "OKNF" }

%% OKM — the same ossia via \magnifyStaff, which unlike the bare NR recipe ALSO scales
%%   every space-alist (music-functions-init.ly:1106-1116 shrinkable-props). Not a ledger
%%   point — it is the model check: OKEY still sits in the shared key column (4.185), and
%%   the scaled alist shows up elsewhere (the ossia's key->time 1.15 * magstep(-3) pulls
%%   the shared TIME column to 7.198 vs 7.535). Committed so the model comparison stays
%%   re-runnable; Lily#'s ossia cites the NR recipe (EngravingDefaults.OssiaScale), so
%%   OKN/OKNF are the twins.
\score { <<
  \new Staff = "main" { \key d \major \time 4/4 d'4 e' fis' g' | a'4 b' cis'' d'' }
  \new Staff \with {
    alignAboveContext = "main"
    firstClef = ##f
    \remove "Time_signature_engraver"
    \override KeySignature.after-line-breaking = #(gd "OKM" "OKEY")
    \override NoteHead.after-line-breaking = #(gd "OKM" "OHEAD")
  } { \magnifyStaff #(magstep -3) \key d \major d''4 e'' fis'' g'' | R1 }
>> \lay "OKM" }

%% TKC / TKT — which staves the KeySignature break-align group is made of. LilyPond's
%%   TabStaff \remove Key_engraver (engraver-init.ly:1214), so a tab staff has NO
%%   KeySignature grob at all: it contributes nothing to the group extent no matter how
%%   many accidentals its own key spells. The two scores carry the SAME notes and differ
%%   ONLY in the tab staff's \key — C major in TKC, F# major (6 sharps) in TKT — so on the
%%   LilyPond side nothing whatever reads the difference and the two dumps are IDENTICAL.
%%   The pair's LP side is an IDENTITY; a Lily# disagreement is its reservation's staff set
%%   alone (the reservation once walked EVERY staff — tab, text row and ossia included —
%%   while the drawing walk skipped tab/text/ossia, so TKT reserved a 6-sharp key nobody
%%   engraves and shoved the first note right of the meter it is spaced from; both walks
%%   now union the ENGRAVED signatures, SpacingRules.ContributesToKeyColumnWidth /
%%   WidestActiveKeyInk).
%%
%%   Ledger quantity = TIME anchor -> first HEAD anchor on the NOTATION staff = 3.300000,
%%   and the 3.3 (not the single-staff 3.7 of KCS/KC2) is the point of the absolute value:
%%   the line-start spring is merge_springs (spring.cc:104) AVERAGING one wish per staff,
%%   and the two staves wish for different things because their last prefatory grob differs.
%%     notation staff: last grob TimeSignature, (first-note . (semi-shrink-space . 2.0))
%%                     -> fixed 6.82+1.0, ideal 7.82+1.0 = 8.82   (staff-spacing.cc:193-198)
%%     tab staff:      last grob Clef,     (first-note . (minimum-fixed-space . 5.0))
%%                     -> fixed = ink-left 1.0 + max(2.6, 5.0) = 6.0 = ideal  (:183-187)
%%                        then the shared min_dist floor (:212-215) lifts it to 8.02
%%     merged:         (8.82 + 8.02) / 2 = 8.42, i.e. TIME ink right 6.82 + 1.6
%%   Two ORDINARY staves keep the 2.0 (their wishes are equal, so the average is that wish):
%%   measured on a treble+bass twin, TIME 5.0034 -> HEAD 8.7034 = 3.700000 exactly. So this
%%   pair also opens the cross-staff wish-averaging regime, which Lily# does not model at
%%   all (it computes ONE system-wide first-note spring): expect the CONTROL to sit ~0.4
%%   off until that is ported.
%%   The TabStaff's own Clef ("TAB", ink 1.0 . 3.6 — WIDER than the G clef, and it is in the
%%   Clef break-align group: TIME sits at 3.6+1.52 = 5.12, not 3.365+1.52) and its
%%   stencil-less TimeSignature are routed to TABCLEF/TABTIME so the notation staff's
%%   CLEF/TIME rows stay unambiguous.
\score { <<
  \new Staff { \key c \major \time 4/4 c'4 d' e' f' | g'2 e' }
  \new TabStaff \with {
    \override Clef.after-line-breaking = #(gd "TKC" "TABCLEF")
    \override TimeSignature.after-line-breaking = #(gd "TKC" "TABTIME")
  } { \key c \major c4 d e f | g2 e }
>> \lay "TKC" }
\score { <<
  \new Staff { \key c \major \time 4/4 c'4 d' e' f' | g'2 e' }
  \new TabStaff \with {
    \override Clef.after-line-breaking = #(gd "TKT" "TABCLEF")
    \override TimeSignature.after-line-breaking = #(gd "TKT" "TABTIME")
  } { \key fis \major c4 d e f | g2 e }
>> \lay "TKT" }

%% TM3 / TM4 — NOT ledger points. The model check behind TKC/TKT's residual: is the
%%   line-start distance really the per-staff AVERAGE (merge_springs, spring.cc:104),
%%   and is the weight really one wish per STAFF? Adding notation staves to the
%%   notation+tab pair changes the average in a way max() and min() cannot fake.
%%   Written as predictions BEFORE the dump, with the tab staff's wish taken as the
%%   8.02 that TKC's 8.42 implies:
%%     TKC (1 notation + 1 tab)  (8.82 + 8.02) / 2       = 8.420000   measured 8.420000
%%     TM3 (2 notation + 1 tab)  (8.82*2 + 8.02) / 3     = 8.553333   measured 8.553333
%%     TM4 (3 notation + 1 tab)  (8.82*3 + 8.02) / 4     = 8.620000   measured 8.620000
%%   A max() model prints 8.820000 for all three, a min() model 8.020000. Both refuted.
%%   TM3 and TM4 also SOLVE for the tab staff's own wish independently -- 8.020000 from
%%   either equation -- which pins staff-spacing.cc:212-215's floor: 8.02 = 0.3 +
%%   min_dist, so the prefatory-to-first-note min_dist of this score is 7.720000. That
%%   is the number Lily# has to reproduce (a skyline distance over ALL staves) before
%%   the averaging can be ported; the other prerequisite is the TAB clef's own ink,
%%   which LilyPond puts at origin 0.8 with ext (1.0 . 3.6) -- WIDER than the G clef's
%%   2.565, and Lily# has never measured its tab clef against it.
\score { <<
  \new Staff { \key c \major \time 4/4 c'4 d' e' f' | g'2 e' }
  \new Staff \with {
    \override Clef.after-line-breaking = #(gd "TM3" "CLEF2")
    \override TimeSignature.after-line-breaking = #(gd "TM3" "TIME2")
    \override NoteHead.after-line-breaking = #(gd "TM3" "HEAD2")
  } { \key c \major c'4 d' e' f' | g'2 e' }
  \new TabStaff \with {
    \override Clef.after-line-breaking = #(gd "TM3" "TABCLEF")
    \override TimeSignature.after-line-breaking = #(gd "TM3" "TABTIME")
  } { \key c \major c4 d e f | g2 e }
>> \lay "TM3" }
\score { <<
  \new Staff { \key c \major \time 4/4 c'4 d' e' f' | g'2 e' }
  \new Staff \with {
    \override Clef.after-line-breaking = #(gd "TM4" "CLEF2")
    \override TimeSignature.after-line-breaking = #(gd "TM4" "TIME2")
    \override NoteHead.after-line-breaking = #(gd "TM4" "HEAD2")
  } { \key c \major c'4 d' e' f' | g'2 e' }
  \new Staff \with {
    \override Clef.after-line-breaking = #(gd "TM4" "CLEF3")
    \override TimeSignature.after-line-breaking = #(gd "TM4" "TIME3")
    \override NoteHead.after-line-breaking = #(gd "TM4" "HEAD3")
  } { \key c \major c'4 d' e' f' | g'2 e' }
  \new TabStaff \with {
    \override Clef.after-line-breaking = #(gd "TM4" "TABCLEF")
    \override TimeSignature.after-line-breaking = #(gd "TM4" "TABTIME")
  } { \key c \major c4 d e f | g2 e }
>> \lay "TM4" }
