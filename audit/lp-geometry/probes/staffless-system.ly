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
%% ★ CHORDS + LYRICS WAS NOT — it is now; read on to "RESOLVED" before believing this
%% paragraph, which is kept because the wrong turn it records is the reason the column dump
%% was run. Widening the first SYLLABLE by 21 ss moves the first chord
%% from 2.312539 to 12.777463 — so something about the lyric DOES reach the first column,
%% while the lyric's own ink stays pinned at 0.000000 in both. Two candidates, not yet
%% told apart:
%%   (a) LyricText joins min_dist (it would have to, to push the column), or
%%   (b) LilyPond clamps a first syllable that would hang off the left edge of the system
%%       back inside it, and the column follows.
%% GUESSING here is exactly what section 5.3 forbids, so the paper column itself is dumped
%% (see the *COL# rows below) and CL/CLW re-run: that says directly whether the COLUMN moved
%% or only the grobs standing on it.
%%
%% ============ PREDICTION, written BEFORE the column dump was run (section 5.0-2) ============
%%
%% The four numbers above are not as mute as the paragraph above them claimed. The chord
%% moved 12.777463 - 2.312539 = 10.464924, and the syllable was widened by
%% 26.904926 - 5.975079 = 20.929847 — EXACTLY twice that, to 15 digits. A factor of one half
%% tied to the syllable's own width is the signature of the syllable being CENTRED on its
%% column. Combine that with the lyric ink staying pinned at 0.000000 in both scores (a
%% clamp: LilyPond will not let the first syllable hang off the left edge) and the column is
%% forced to sit at half the syllable width. So:
%%
%%   predicted first musical column X   CL  =  5.975079 / 2 =  2.987540
%%                                      CLW = 26.904926 / 2 = 13.452463
%%   predicted CHORD offset ON that column, both scores = -0.675000  (a constant, since
%%                                      2.312539 - 2.987540 = 12.777463 - 13.452463)
%%
%% If the dump shows those column X values, candidate (b) holds and (a) is dead: the lyric
%% does NOT price the column through min_dist, it is placed by self-alignment against a
%% clamped left edge and the column follows it. If instead the column sits at 2.312539 /
%% 12.777463 (i.e. under the chord), the chord is what is column-aligned and the lyric is
%% hanging off it — a different port entirely. Either way the -0.675000 constant then needs
%% a LilyPond source before it may enter code (section 5.2).
%%
%% ============ THE PREDICTION WAS WRONG — column dump, 2026-07-25 ============
%%
%%   score  LYRIC ink left   LYRIC's column   CHORD ink left   CHORD's column
%%   CL       0.000000         2.312539         2.312539         2.312539
%%   CLW      0.000000        12.777463        12.777463        12.777463
%%
%% (Every previously recorded figure reproduced unchanged in the same run, so the added dump
%% perturbs nothing.)
%%
%% Three facts, and the predicted "column at w/2" is not among them:
%%
%%   1. THE CHORD IS THE COLUMN. ChordName anchor == its column's X in every score here
%%      (CO, COW, CS too). The chord never moves relative to its column; what moved between
%%      CL and CLW is the COLUMN.
%%   2. THE LYRIC HANGS LEFT OF ITS COLUMN, by w/2 - 0.675000 — and that offset is LilyPond's
%%      own formula, not a fitted constant. self-alignment-interface.cc:117-176
%%      (`aligned_on_parent`) computes
%%          x = -ext.linear_combination (self_align) + he.linear_combination (par_align)
%%      where, because the column holds NO note heads, `he` falls back to the PaperColumn's
%%      placeholder `X-alignment-extent = (0 . 1.35)` (define-grobs.scm:2750, "as wide as a
%%      note head"; self-alignment-interface.cc:134-139). LyricText's `self-alignment-X` is
%%      `left-align-at-split-notes` (define-grobs.scm:2222), which returns CENTER when there
%%      are no note heads to ask (output-lib.scm:1671-1673), and `parent-alignment-X` is ()
%%      so it copies self_align (:156-157). With ext = (0 . w) that is
%%          x = -w/2 + 1.35/2 = -w/2 + 0.675
%%      ⇒ CL  -2.987540 + 0.675 = -2.312540   ⇒ ink left 2.312539 - 2.312540 = 0.000000
%%         CLW -13.452463 + 0.675 = -12.777463 ⇒ ink left 0.000000
%%      THE 0.675 IS 1.35/2. It is not a clamp constant; the syllable is simply centred on a
%%      note-head-sized placeholder.
%%   3. THE LYRIC'S INK LEFT LANDS ON 0.000000 IN BOTH SCORES. Given fact 2 that is not a
%%      property of the lyric at all — it says the COLUMN sits at exactly the position where
%%      the syllable's leftmost ink touches the system's left edge. So something DOES push
%%      the column right by the amount the syllable overhangs it, and the answer to the
%%      original question is: THE COLUMN MOVED, the grobs on it did not.
%%
%% What is still open is which mechanism does the pushing, and the four numbers still do not
%% say. Both of these produce "ink left = 0.000000":
%%   (a) a ROD from the left-edge column, i.e. set_column_rods (spacing-spanner.cc:228-297)
%%       reading the column's LEFT horizontal skyline, which contains the overhanging lyric
%%       ("stickout will be negative if the right-hand column sticks out a lot to the left",
%%       :254-255), OR
%%   (b) the spring's own min_dist growing with the overhang.
%% They differ in what happens when the overhang goes away, so the next two scores remove it
%% two different ways instead of arguing.
%%
%% ============ RESOLVED — perturbation 2, measured 2026-07-25 ============
%%
%%   score  first column   LYRIC ink left   CHORD ink left     (predictions were CLX 2.987540
%%   CL       2.312539       0.000000         2.312539          / CLL 0.500000; both hit)
%%   CLX      2.987539       0.000000         2.987539
%%   CLL      0.500000       0.500000         0.500000
%%
%% CLL is the decisive one: take away the syllable's REACH to the left of its column — the
%% music, the syllable and its width are untouched — and the column falls straight back to
%% 0.500000, the chords-only answer. So the column is not priced by the lyric's width. It is
%% pushed by the lyric's OVERHANG, and the size of the push is the overhang itself: CL's
%% 2.312539 carries no `+ 0.5` on top.
%%
%% CL vs CLX confirms the shape from the other side: every column moved by exactly 0.675000
%% and the column-to-column distances are identical to 6 digits (2nd chord minus 1st is
%% 4.845920 in both). Only the FIRST gap changed. That is one rod at the head of the line,
%% not a respacing.
%%
%% THE MECHANISM, entirely from LilyPond source (no fitting):
%%
%%   1. THE ROD. simple-spacer.cc:431-432 gives every column but the line starter
%%          description.keep_inside_line_ = col->extent (col, X_AXIS);
%%      when `keep-inside-line` is set — and it is #t by default on both PaperColumn
%%      (define-grobs.scm:2742) and NonMusicalPaperColumn (:2525); the property means "this
%%      column cannot have objects sticking into the margin"
%%      (define-grob-properties.scm:637). simple-spacer.cc:556-560 then adds
%%          spacer.add_rod (i, cols.size (), cols[i].keep_inside_line_[RIGHT]);
%%          spacer.add_rod (0, i, -cols[i].keep_inside_line_[LEFT]);
%%      The second rod is this defect's whole answer: the first musical column must stand at
%%      least `-extent[LEFT]` away from the line-start column. No padding, no spring term —
%%      which is why the measurement is the bare overhang.
%%      ⇒ first column X = max (standard_breakable_column_spacing's min_dist + 0.5,
%%                              -column_extent[LEFT])
%%      CO/COW/COK/CO3: a ChordName never reaches left of its column, so the rod is 0 and the
%%      spring's 0.5 stands. CL/CLW/CLX: the rod wins. CLL: the rod is 0 again.
%%
%%   2. WHY A SYLLABLE REACHES LEFT AND A CHORD NAME DOES NOT.
%%      LyricText has (X-offset . ,ly:self-alignment-interface::aligned-on-x-parent)
%%      (define-grobs.scm:2229). In aligned_on_parent (self-alignment-interface.cc:117-176)
%%          x = -ext.linear_combination (self_align) + he.linear_combination (par_align)
%%      * self_align = `left-align-at-split-notes` (define-grobs.scm:2222), which asks the
%%        parent for note heads and returns CENTER when there are none
%%        (output-lib.scm:1642-1673) — a staff-less lyric always takes that branch;
%%      * par_align = `parent-alignment-X` = () (define-grobs.scm:2221) ⇒ copies self_align
%%        (self-alignment-interface.cc:156-157);
%%      * `he` is the PARENT's extent, and for a PaperColumn holding no note heads it falls
%%        back to the placeholder `X-alignment-extent = (0 . 1.35)`, "as wide as a note head"
%%        (self-alignment-interface.cc:121-139, define-grobs.scm:2749-2750). LilyPond's own
%%        comment names this exact situation: "This situation happens for lyrics without
%%        `associatedVoice`, for example."
%%      With ext = (0 . w) that is  x = -w/2 + 1.35/2 = -w/2 + 0.675.
%%      ⇒ overhang = w/2 - 0.675:  CL 2.312540, CLW 12.777463, CLX (placeholder forced to
%%        (0 . 0)) 2.987540 — each equal to the measured column to 6 digits.
%%      THE 0.675 IS 1.35/2, NOT A CLAMP CONSTANT. Nothing in LilyPond clamps a syllable to
%%      the margin; the syllable is centred on a note-head-sized placeholder and the ROD then
%%      moves the COLUMN until the ink clears the margin, which is why the ink lands on
%%      0.000000 exactly.
%%      ChordName, by contrast, declares no X-offset and no self-alignment-interface at all
%%      (define-grobs.scm:837-855), so it sits on its column's reference point with offset 0
%%      — which is why CHORD anchor == column X in every score dumped here.
%%
%% CONSEQUENCE FOR THE PORT. The two halves are no longer coupled: CO's `min_dist + 0.5` and
%% CL's rod are separate LilyPond mechanisms, so porting the staff-less line-start spring no
%% longer requires guessing at the lead-sheet case. What CL additionally needs is the
%% keep-inside-line rod, which Lily# does not have at all — and note it is NOT staff-less
%% specific: it is a general left- AND right-margin constraint on every column of every
%% system, so it belongs with the spacer, not with the staff-less special case.
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

%% Each dumped grob produces TWO records:
%%   PROBE <tag> <NAME>       — the grob's own anchor and ink extent, as everywhere else
%%   PROBE <tag> <NAME>COL#<rank> — the X of the PAPER COLUMN it hangs from (its X-parent),
%%       in the same system frame. `ext=(0 . 0)` there is a PLACEHOLDER, not a measurement:
%%       a paper column has no ink of its own, and the field exists only so the shared
%%       parser (Measure-LilyPondGeometry.ps1, which treats an unparsable PROBE line as a
%%       fatal hole) accepts the row. Read the anchor, ignore the ink.
%% The pair is what tells a column that MOVED apart from a grob that slid along a column
%% that did not.
#(define ((gd tag name) g)
   (let* ((sys (ly:grob-system g))
          (col (ly:grob-parent g X)))
     (format #t "\nPROBE ~a ~a x=~a ext=~a\n" tag name
             (ly:grob-relative-coordinate g sys X)
             (ly:grob-extent g g X))
     (format #t "\nPROBE ~a ~aCOL#~a x=~a ext=(0 . 0)\n" tag name
             (ly:grob-property col 'rank)
             (ly:grob-relative-coordinate col sys X))))

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

%% ---- PERTURBATION 2 (2026-07-25): remove the overhang, two different ways ----
%% Both scores are the SAME MUSIC as CL. Only the lyric's alignment against its column
%% changes, i.e. only how far the syllable reaches LEFT of the column it hangs from. If the
%% column is placed by something that reads that reach (a rod off the left-edge column, or a
%% min_dist that grew), the column must come back left when the reach is removed. If the
%% column is priced by the syllable's WIDTH regardless of where the syllable sits, it will
%% not move at all.
%%
%% PREDICTIONS, written before running (section 5.0-2):
%%
%%   CLX — X-alignment-extent forced to (0 . 0), so the 0.675 half-placeholder disappears
%%         and aligned_on_parent gives x = -w/2 exactly. The syllable now reaches
%%         2.987540 left of its column instead of 2.312540.
%%         predict  column 2.987540, LYRIC ink left 0.000000, CHORD 2.987540
%%         (falsifier: column stays 2.312539 and the lyric hangs off the page at -0.675001,
%%          which would mean the column never knew about the lyric.)
%%
%%   CLL — self-alignment-X forced to LEFT. Then aligned_on_parent gives
%%         x = -ext.linear_combination(-1) + he.linear_combination(-1) = -0 + 0 = 0, so the
%%         syllable's ink left sits exactly ON its column and reaches nothing.
%%         predict  column 0.500000 (back to the chords-only `min_dist + 0.5`),
%%                  LYRIC ink left 0.500000, CHORD 0.500000
%%         (falsifier: column stays 2.312539, i.e. the width was priced, not the reach.)

%% CLX — CL with the PaperColumn placeholder width taken away.
\score {
  <<
    \new ChordNames { \time 4/4 \harmony }
    \new Lyrics \lyricmode { Twin2 -- kle4 twin -- kle | lit2 -- tle | star1 }
  >>
  \layout {
    \lay "CLX"
    \context { \Score \override PaperColumn.X-alignment-extent = #'(0 . 0) }
  }
}

%% CLL — CL with the syllable left-aligned on its column instead of centred.
\score {
  <<
    \new ChordNames { \time 4/4 \harmony }
    \new Lyrics \with { \override LyricText.self-alignment-X = #LEFT }
      \lyricmode { Twin2 -- kle4 twin -- kle | lit2 -- tle | star1 }
  >>
  \lay "CLL"
}

%% ---- PERTURBATION 3 (2026-07-25): the NARROW-syllable regime, where LilyPond's own
%%      offset is too small to reach past the spring ----
%%
%% WHY THESE EXIST. The Lily# twin for CL cannot be a raw comparison: the column stands at
%% w/2 - 0.675 on LilyPond's side and at w/2 on Lily#'s, and the two engravers do not measure
%% the same w (Lily# draws lyrics in its own serif at 3.2 ss and is ~27% wider than LilyPond
%% here — "Twin" is 7.587200 against LilyPond's 5.975079). A point on CL would therefore mix
%% the missing 0.675 with a text-metric difference and could not say which is which.
%%
%% A NARROW syllable separates them, because the rod is a MINIMUM. LilyPond's reach is
%% w/2 - 0.675, so for any syllable under 2.35 ss wide the reach is below the 0.5 that
%% standard_breakable_column_spacing already gives, the rod is slack, and the column stands
%% on 0.500000 — the chords-only answer, with NO font metric in it at all. Lily#'s reach is
%% w/2 with no placeholder, so it clears 0.5 for anything wider than 1.0 ss and the column
%% leaves the floor. Both scores below sit in that window on both sides
%% ("I" = 1.302400 and "a" = 1.779200 in Lily#'s metric, so its reaches are 0.651200 and
%% 0.889600; LilyPond's are narrower still, so both are far under 2.35).
%%
%% That makes BOTH points identities on LilyPond's side, which is the strongest form of pair
%% (section 5.0): LilyPond's difference is 0 by construction, so whatever Lily# reports IS
%% the defect's size, in Lily#'s own units, with no LilyPond metric to contaminate it.
%%
%% The lyric line is deliberately MINIMAL and rhythmically identical on both sides: two
%% half-note syllables per bar, matching the two half-note chords, because Lily# spreads a
%% row's syllables evenly across the bar while LilyPond reads their written durations. Two
%% syllables of equal length is where the two agree exactly (section 5.0: confirm the pair is
%% the same music).
%%
%% PREDICTIONS, written before running (section 5.0-2):
%%
%%   CLI — first syllable "I". predict column 0.500000, i.e. CLI - CO = 0.000000 exactly.
%%   CLA — first syllable "a". predict column 0.500000, i.e. CLA - CLI = 0.000000 exactly.
%%   (falsifier for both: a column above 0.5, which would mean LilyPond's "I"/"a" are wider
%%    than 2.35 ss and the scores missed the window. The LYRIC ext dumped below says so
%%    directly — read it before believing the identity.)
%%   Lily# side, predicted from its own metric: CLI - CO = 1.302400/2 - 0.5 = +0.151200 and
%%   CLA - CLI = (1.779200 - 1.302400)/2 = +0.238400. Porting the placeholder takes both to
%%   0, because w/2 - 0.675 is then negative for these syllables and Lily# lands on the same
%%   0.5 floor.

%% CLI — chords + lyrics, first syllable NARROW ("I").
\score {
  <<
    \new ChordNames { \time 4/4 \harmony }
    \new Lyrics \lyricmode { I2 no2 | oh2 no2 | yes1 }
  >>
  \lay "CLI"
}

%% CLA — the same, one letter wider ("a"). Still inside LilyPond's floor window.
\score {
  <<
    \new ChordNames { \time 4/4 \harmony }
    \new Lyrics \lyricmode { a2 no2 | oh2 no2 | yes1 }
  >>
  \lay "CLA"
}

%% ---- THE OTHER BRANCH of the same formula (2026-07-25): lyrics UNDER NOTE HEADS ----
%%
%% aligned_on_parent takes `he` from the parent column's note-column-interface extent and only
%% falls back to the (0 . 1.35) placeholder when that is EMPTY
%% (self-alignment-interface.cc:117-141). Every score above exercises the fallback. LSH
%% exercises the branch that is normally taken, so the port can write BOTH (section 5.2: write
%% every branch) instead of the staff-less one alone.
%%
%% THE QUANTITY IS THE SYLLABLE'S INK CENTRE MINUS ITS COLUMN, and it is free of every text
%% metric: with self_align = par_align = CENTER,
%%     ink centre = column + x + w/2 = column + (-w/2 + he.centre) + w/2 = column + he.centre
%% so w cancels identically. That is what makes this comparable with Lily# directly — unlike
%% the raw anchor, which carries half a syllable width in a face the two engravers do not
%% share. The staff-less scores above already answer it: CLI and CLA both put the ink centre
%% on 1.175000 over a column at 0.500000, i.e. 0.675000 = 1.35/2 exactly, for two syllables of
%% different widths.
%%
%% PREDICTION for LSH, written before running (section 5.0-2): the syllable's ink centre sits
%% he.centre right of its column, and here `he` is the note head's own extent, so the answer is
%% half a note head — NOT 0.675. The dumped HEAD ext says what half a note head is, and the
%% two must agree to 6 digits.
%%   (falsifier: 0.675000 again, which would mean the placeholder is used even when note heads
%%    are present and the note-column branch is dead code for lyrics.)

%% LSH — a plain vocal line: one staff, one syllable per note, no chords.
\score {
  <<
    \new Staff { \time 4/4 c'2 a' | f'2 g' | c'1 }
    \new Lyrics \lyricmode { I2 no2 | oh2 no2 | yes1 }
  >>
  \lay "LSH"
}

%% LSA — LSH with an ACCIDENTAL on the first note. WHAT IT DECIDES: `he` is
%%   robust_relative_extent of each NOTE COLUMN (paper-column.cc get_interface_extent), and a
%%   Note_column is an axis group — so whether an accidental standing left of the head joins
%%   that extent decides what a port must union. The question is not answerable from LSH,
%%   where the column holds a bare head.
%%
%%   PREDICTION, written before running (section 5.0-2): the accidental IS in the note column,
%%   so `he` becomes (-a . w_head), its centre drops below half a head, and the syllable moves
%%   LEFT relative to its head — offset < 0.688700 by half the accidental's reach.
%%   (falsifier: 0.688700 unchanged, which would mean the note column's extent is its heads
%%    alone and a port may union heads only.)
%%
%%   ★ THE PREDICTION WAS WRONG AND THE FALSIFIER FIRED — measured 2026-07-25:
%%       LSA  HEAD anchor 9.335000 (= its column)   LYRIC ink [9.528622 10.518778]
%%            ink centre 10.023700 - 9.335000 = 0.688700, IDENTICAL to LSH to 6 digits.
%%   The sharp stands left of the column and changes nothing. So `he` is the note HEADS' own
%%   extent, and a port must union HEADS — NOT the column's musical ink, which is a different
%%   and wider quantity (SpacingRules.MusicalInkOverhangsPerColumn deliberately includes the
%%   accidental's leftward reach, because the keep-inside-line rod DOES take it). Using that
%%   one here would have moved every accidental-bearing syllable left by half the accidental.
%%   This is the section 5.0 case for writing predictions down: the wrong direction named the
%%   quantity to port.
\score {
  <<
    \new Staff { \time 4/4 cis'2 a' | f'2 g' | c'1 }
    \new Lyrics \lyricmode { I2 no2 | oh2 no2 | yes1 }
  >>
  \lay "LSA"
}

%% LSD / LSR — the last two things a port has to know about `he`, since a NoteColumn's
%%   X-extent is its whole axis group (define-grobs.scm NoteColumn X-extent =
%%   ly:axis-group-interface::width) and the accidental turned out NOT to be in it.
%%
%%   PREDICTIONS, written before running (section 5.0-2):
%%     LSD — the first note is DOTTED. A dot sits right of the head, so if it is in the group
%%           the extent grows rightward and the syllable moves RIGHT: offset > 0.688700.
%%     LSR — the first column is a REST. A rest is held by its note column, so `he` should be
%%           the REST's extent and the offset half a rest — a quarter rest is narrower than a
%%           head, so predict BELOW 0.688700.
%%     (falsifier for either: 0.688700 unchanged, i.e. the extent really is the note heads
%%      alone and a port unions heads and nothing else.)
%%
%%   ★ MEASURED 2026-07-25 — the dot prediction was WRONG, the rest one held:
%%       LSD  HEAD anchor 8.585000  LYRIC ink [8.778622 9.768778]  centre - column = 0.688700
%%            IDENTICAL to LSH. A DOT IS NOT IN `he`.
%%       LSR  column 8.585000       LYRIC ink [8.839922 9.830078]  centre - column = 0.750000
%%            A REST IS, and it replaces the head that is not there (half rest, so half of 1.5).
%%
%%   ⇒ WHAT A PORT MUST UNION, decided by measurement rather than by reading an axis group:
%%      note heads and rests, NOT accidentals (LSA) and NOT dots (LSD). That is consistent
%%      with LilyPond's own structure — a Dots grob hangs off its NOTE HEAD and the
%%      accidentals off an Accidental_placement, so neither is among the note column's
%%      `elements`, which is what ly:axis-group-interface::width unions (define-grobs.scm
%%      NoteColumn X-extent). A stem is in the group but never widens it: it stands at the
%%      head's own edge.
\score {
  <<
    \new Staff { \time 4/4 c'2. a'4 | f'2 g' | c'1 }
    \new Lyrics \lyricmode { I2. no4 | oh2 no2 | yes1 }
  >>
  \lay "LSD"
}

\score {
  <<
    \new Staff { \time 4/4 r2 a' | f'2 g' | c'1 }
    \new Lyrics \lyricmode { I2 no2 | oh2 no2 | yes1 }
  >>
  \lay "LSR"
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
