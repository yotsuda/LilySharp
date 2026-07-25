\version "2.26.0"
%% LP FIDELITY PROBE — a system with NO STAFF (ChordNames / Lyrics only).
%%
%% Produces the numbers in ../lp-geometry.json under "staffless.*". Run it with
%% ../Measure-LilyPondGeometry.ps1 -Probe staffless-system.ly.
%%
%% WHY THIS EXISTS. Lily# prices the first column of such a system from a PREFIX it
%% reserves — a clef and a meter — and no row engraves either. The reservation is not a
%% deliberate choice: SpacingRules.ClefGroupExtent falls back to the treble G when NO staff
%% contributes a clef stencil (a chord / lyric row is skipped, so the set comes out empty),
%% and MultiStaffLayouter's prefixHasTime never asks whether any staff engraves a meter.
%% That is the SAME shape as the defect the ledger closed under
%% line-start.time-to-first-note.tab-keyed, where the KEY column was booked for staves that
%% engrave none; the clef and meter columns were left unfixed.
%%
%% It matters because it decides which spring LilyPond's own code even reaches. With no
%% Staff_spacing wish in the left column, spacing-spanner.cc:514-515 falls to
%% standard_breakable_column_spacing, i.e. spacing-basic.cc:71-82's `ideal = min_dist + 0.5`
%% for a dt == 0 pair. Porting THAT literally on top of Lily#'s phantom prefix produced a
%% NEGATIVE spring (the ideal is column-relative, and Lily# then subtracts a prefix right
%% edge of ~6.585 that stands for ink nobody draws), so the port was declined and a
%% LILYSHARP-OWN fallback left in its place. This probe is what decides that properly.
%%
%% ⚠️ A staff-less system is NOT a Lily# extension. `\new ChordNames` alone and a lead sheet
%% of ChordNames + Lyrics are ordinary LilyPond; an earlier comment in LineStartColumn.cs
%% claiming "LilyPond has no such system" was simply wrong.
%%
%% ragged-right, indent 0: every spring at force 0, so what is read is the ideal.
%%
%% ============ MEASURED 2026-07-25 on LilyPond 2.26.0 ============
%%
%%   score  first ChordName anchor (= its ink LEFT; extent is (0 . w))
%%   CO     0.500000     chords only, 4/4
%%   CO3    0.500000     the SAME, 3/4          -> identity, to 15 digits
%%   COK    0.500000     the SAME, E major      -> identity, to 15 digits
%%   CS     8.585000     the same chords OVER A STAFF
%%
%% CO reads standard_breakable_column_spacing EXACTLY: min_dist is 0 (the prefatory column
%% engraves nothing, so there is no box on either side), and spacing-basic.cc:71-82 gives
%% `ideal = min_dist + 0.5` for a dt == 0 pair. 0.500000 is that 0.5.
%%
%% CS is the familiar 8.585000 — the same number probe SKC and JN already pin, and the one
%% LineStartColumnTests.MeteredLineStart_SpringIsLilyPonds asserts Lily# reproduces. So the
%% staff-ful case is ALREADY right and the whole of the defect is the staff-less one.
%% CS - CO = 8.085000 is what a staff earns, and Lily# books ~all of it either way.
%%
%% CL (chords + lyrics, no staff) is dumped too and is the lead-sheet regime: first LYRIC
%% anchor 0.000000, first CHORD 2.312539. Note the two are NOT at the same X even though
%% they share a moment — so a Lily# twin for CL must decide which grob it measures before
%% it can compare. Not yet ledgered.
%%
%% ============ PERTURBATION, measured the same day ============
%%
%%   score  first CHORD ink left   first LYRIC ink left
%%   CO      0.500000              -            chord name 1.877882 wide
%%   COW     0.500000              -            chord name 15.410322 wide
%%   CL      2.312539              0.000000     first syllable 5.975079 wide
%%   CLW    12.777463              0.000000     first syllable 26.904926 wide
%%
%% ★ CHORDS ONLY IS SETTLED. Widening the chord name by 13.5 ss does not move the first
%% column by a thousandth: the ChordName does NOT reach left into
%% Paper_column::minimum_distance, so min_dist stays 0 and `min_dist + 0.5` is the whole
%% answer. That half is ready to port.
%%
%% ★ CHORDS + LYRICS IS NOT. Widening the first SYLLABLE by 21 ss moves the first chord
%% from 2.312539 to 12.777463 — so something about the lyric DOES reach the first column,
%% while the lyric's own ink stays pinned at 0.000000 in both. Two candidates, not yet
%% told apart:
%%   (a) LyricText joins min_dist (it would have to, to push the column), or
%%   (b) LilyPond clamps a first syllable that would hang off the left edge of the system
%%       back inside it, and the column follows.
%% The arithmetic does not separate them from these four numbers alone (neither "centred on
%% the column" nor "left-aligned at the column" reproduces both scores), and GUESSING here
%% is exactly what section 5.3 forbids. NEXT MEASUREMENT: dump the paper column itself
%% (-ddebug-paper-columns) and re-run CL/CLW, which says directly whether the COLUMN moved
%% or only the grobs on it.
%%
%% This matters because the lead-sheet fixtures are all the CL shape, and both halves go
%% through ONE Lily# code path — so the CO half cannot be ported alone without guessing at
%% the CL half.
%%
%% ⚠️ ANCHOR CONVENTION, unresolved for the Lily# twin: LilyPond's ChordName reference point
%% IS its ink left (the extent above is (0 . w)), while Lily# draws a chord name with
%% text-anchor="middle", i.e. it records the CENTRE. A twin that compares the two raw
%% numbers would carry half a chord-name width as a constant and mask the defect. The
%% frame-free quantity is CS - CO, which cancels the convention because both sides use
%% their own consistently; that is what the ledger point should be.

\header { tagline = ##f }

%% Dumps go to STDOUT; keep stderr on its own stream (the script does) — see
%% barline-spacing.ly's header for what happens when they are merged.

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
         \override ChordName.after-line-breaking    = #(gd tag "CHORD")
         \override LyricText.after-line-breaking    = #(gd tag "LYRIC")
         \override BarLine.after-line-breaking      = #(gd tag "BAR")
         \override Clef.after-line-breaking         = #(gd tag "CLEF")
         \override TimeSignature.after-line-breaking = #(gd tag "TIME")
         \override NoteHead.after-line-breaking     = #(gd tag "HEAD")
       }
     }
   #})

harmony = \chordmode { c2 a:m | f2 g:7 | c1 }

%% CO — chords ONLY, 4/4. The quantity is the system's left edge (x = 0 in this frame) to
%%   the FIRST ChordName. Nothing prefatory is engraved, so this reads the whole of what
%%   LilyPond puts in front of the first column.
\score { \new ChordNames { \time 4/4 \harmony } \lay "CO" }

%% CO3 — the IDENTITY twin: the SAME chords under 3/4. A ChordNames context has no
%%   Time_signature_engraver, so LilyPond draws no meter either way and CO3's first chord
%%   must land on CO's to 15 digits. Lily# reserves GetTimeSigWidth(beats, beatType), which
%%   is NOT the same for 4/4 and 3/4 — so the Lily# halves differ by exactly the meter
%%   width it books for a meter nobody engraves. LilyPond's difference being 0 is what makes
%%   the Lily# difference the defect's own size.
%%   (The bar lines fall differently under 3/4; only the FIRST chord is compared.)
\score { \new ChordNames { \time 3/4 \harmony } \lay "CO3" }

%% COK — the second identity: the same chords in E major (4 sharps). ChordNames has no
%%   Key_engraver either, so again LilyPond is unmoved. Lily# already books nothing here
%%   (SpacingRules.ContributesToKeyColumnWidth excludes a text row), so this half is
%%   expected to be an identity on BOTH sides — a control that the key half of the
%%   reservation is genuinely closed and only the clef and meter halves are open.
\score { \new ChordNames { \time 4/4 \key e \major \harmony } \lay "COK" }

%% CL — chords AND lyrics, still no staff: the lead-sheet regime, where Lily# takes a
%%   different path again (LyricSpacing / ApplyChordRowSpacing). Same first-chord question.
\score {
  <<
    \new ChordNames { \time 4/4 \harmony }
    \new Lyrics \lyricmode { Twin2 -- kle4 twin -- kle | lit2 -- tle | star1 }
  >>
  \lay "CL"
}

%% ---- PERTURBATION (section 5.3): does a ChordName / LyricText join min_dist? ----
%% If the first column's position depends on the WIDTH of the text sitting on it, that text
%% is in Paper_column::minimum_distance; if it does not, the text is priced some other way
%% and the column is fixed by `min_dist + 0.5` alone. Vary ONE thing per score.

%% COW — CO with a much WIDER first chord name. Same music otherwise.
\score { \new ChordNames { \time 4/4 \chordmode { c:maj9.11+ 2 a:m | f2 g:7 | c1 } } \lay "COW" }

%% CLW — CL with a much LONGER first syllable.
\score {
  <<
    \new ChordNames { \time 4/4 \harmony }
    \new Lyrics \lyricmode { Twinkletwinkletwinkle2 -- kle4 twin -- kle | lit2 -- tle | star1 }
  >>
  \lay "CLW"
}

%% CS — the CONTROL that is NOT staff-less: the same chords over an ordinary staff. Here
%%   LilyPond DOES engrave a clef and a meter, so the first chord sits far right, and the
%%   difference CS - CO is the size of the prefix a staff earns. Lily# should agree on THIS
%%   one already; if it does not, the defect is not confined to the staff-less case.
\score {
  <<
    \new ChordNames { \time 4/4 \harmony }
    \new Staff { \time 4/4 c'2 a' | f'2 g' | c'1 }
  >>
  \lay "CS"
}
