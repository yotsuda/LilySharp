\version "2.26.0"
%% LP FIDELITY PROBE — WHERE A REHEARSAL MARK SITS WHEN A GRACE NOTE'S FLAG STANDS UNDER IT.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe mark-grace.ly -Prefix PROBEG
%% (or: lilypond -dno-print-pages mark-grace.ly, and read the PROBEG lines).
%%
%% THE DEFECT THIS MEASURES (owner, session 568, on the showcase's petite-valse): the section
%% label B of the second system printed straight through the flag of the `grace { gis''16 }`
%% that opens the bar under it. Lily#'s inside-staff profile (SkylineBuilder) skipped every
%% grace-time item, so a grace was in NO vertical skyline and the outside-staff movers cleared
%% thin air above it.
%%
%% LILYPOND'S MECHANISM: a grace body is an ordinary stretch of an ordinary Voice
%% (ly/engraver-init.ly has no `\name Grace`, only `\consists Grace_engraver` inside
%% `\name Voice`), so its NoteHead / Stem / Flag / Accidental are inside-staff grobs of the
%% staff's VerticalAxisGroup like any other note's — lily/axis-group-interface.cc:914-935
%% inside_staff_skylines collects every element with no outside-staff-priority, and
%% general-grace-settings (scm/music-functions.scm:636-650) states only their font-size. The
%% RehearsalMark (outside-staff-priority 1500, padding 0.8) is then placed by
%% add_grobs_of_one_priority against that profile, clearing the flag by outside-staff-padding
%% 0.46 exactly as it clears a full-size stem.
%%
%% THE PAIR (RULES 5.0): MGF and MGN are ONE VARIABLE apart — the grace — and nothing else.
%% MGN reads the plain 2.850000 (padding 0.8 over the staff's top line, i.e. 2.05 + 0.8 over
%% the refpoint), the number mark.plain.staff-to-baseline already holds; MGF reads the
%% grace flag's top + 0.46.
%%
%% PREDICTION, written before running: MGF's mark baseline sits at the grace flag's top plus
%% 0.460000 above the staff refpoint, well above MGN's 2.850000 — the grace `gis'` (G#5, one
%% space over the top line) with a forced-up stem of 0.8 × the sixteenth's length puts its
%% flag's top about 3 spaces over the top line. FALSIFIER: an MGF reading equal to MGN's means
%% LilyPond does NOT let the grace into the profile the mark clears, and the port would be
%% wrong to seed it.
%%
%% The music is `lysc ly` of Lab sessions/p568/mark-grace.lys — `\fixed c'` with the SAME
%% spellings Lily# wrote (Lily#'s absolute `gis'` is LilyPond's `gis'` under \fixed c', i.e.
%% G#5; HANDOFF 6), the marks BOXED as the exporter writes them (`\mark \markup { \box "B" }`)
%% — with ONE hand edit: the mark written BEFORE the grace, LilyPond's idiom for a marked
%% bar that opens with a grace (the exporter writes it after, which lands the mark on the
%% grace's own column rather than the line-start anchor — a twin ordering defect, not a Y
%% one).
%% ⚠️ BOXED ON BOTH SIDES, unlike mark-chord-row.ly's books, and on purpose: the mark is
%% placed by its SKYLINE, and a bare letter's outline is a rounded bowl where a box's is a
%% flat edge. MEASURED with `\mark "B"` bare (first run of this probe): the mark's baseline
%% read 4.843 over the refpoint with the grace stem's top at 5.1 — the letter's rounded
%% lower-right corner slid down the stem's padded 45° flank — a number no box can read.
%% Lily# draws its @mark as a box, so the LilyPond side is boxed too and the two engines
%% clear the flag with the same flat edge; the reading is the BASELINE above the staff
%% refpoint on both sides (LilyPond's boxed markup extends below the baseline by its
%% box-padding and thickness, as Lily#'s frame does).

#(define (dump tag layout pages)
   (for-each
    (lambda (page)
      (for-each
       (lambda (sys)
         (let ((sg (ly:prob-property sys 'system-grob)))
           (if (ly:grob? sg)
               (let ((all (ly:grob-object sg 'all-elements)))
                 (if (ly:grob-array? all)
                     (for-each
                      (lambda (g)
                        (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                          (if (memq nm '(RehearsalMark StaffSymbol NoteHead Stem Flag
                                         Accidental BarNumber Clef))
                              (format #t "PROBEG ~a ~a rel=~a ext=(~a . ~a) X=~a xext=(~a . ~a)\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))
                                      (ly:grob-relative-coordinate g sg X)
                                      (car (ly:grob-extent g g X))
                                      (cdr (ly:grob-extent g g X))))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))
probeG =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEG BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})

%% ⚠️ THE GRACE OPENS A SECOND SYSTEM, deliberately — the showcase's shape. On a first system
%% the meter stands between the clef and the first column, the mark anchors on the clef's
%% right (X 3.365) and the grace's flag stands past X 9.5: the two never meet in X, and
%% LilyPond reads the plain 2.850000 with the grace present (MEASURED first, one-system book:
%% mark rel −2.25 over staff −5.10, flag at X 9.58..10.17). The second system has no meter,
%% the grace column stands right after the clef, and the mark's X window (3.365 .. 5.38) meets
%% the flag's — which is where the showcase's label printed through it.
%%
%% MGF -- the mark over a GRACE opening the second system:
%%   `c'4@mark("A") d' e' f' | ... | break` then `grace { gis'16 } a'4@mark("B") b' a' g' | ...`.
\book {
  \probeG "MGF"
  \score {
    \new Staff \fixed c' {
      \time 4/4
      \key c \major
      \mark \markup { \box "A" } c'4 d' e' f' |
      g' a' b' c'' |
      c''4 b' a' g' |
      f' e' d' c' |
      \break
      \mark \markup { \box "B" } \grace { gis'16 } a'4 b' a' g' |
      f'4 e' d' c' |
    }
  }
}

%% GCL / GCN -- THE OTHER READER OF THE SAME PROFILE: the STAFF-TO-STAFF gap over a grace.
%% A mark is placed against the profile from ABOVE; the staff alignment reads it from the
%% side, so the two are different consumers of one silhouette and a repair can reach one
%% without the other. The music is the repo fixture test/grace-lower-staff cut to its shape:
%% a treble staff over a bass staff whose bars OPEN with a beamed grace, its stems forced up
%% and its beam reaching into the gap between the staves.
%%
%% ⚠️ SEEDED AFTER THE REPAIR, not before it, and the ledger entry says so: this quantity was
%% found by a SNAPSHOT that moved (test/grace-lower-staff) once the profile carried the
%% grace, and the point exists to lock what the snapshot re-based to. The LilyPond number
%% below is the independent datum either way.
\book {
  \probeG "GCL"
  \score {
    <<
      \new Staff { \clef "treble" \fixed c' { \time 4/4 \key c \major c'4 d' e' f' | g'1 | } }
      \new Staff { \clef "bass" \fixed c' {
        \time 4/4 \key c \major
        \grace { e,16 f, } g,4 a, b, c |
        \grace { d16 e } f2 g2 | } }
    >>
  }
}

%% GCN -- the control: the same two staves with NO grace.
%% ⚠️ THE SECOND BAR'S GRACE IS THE ONE THAT BINDS, and the first draft of this pair got it
%% wrong: written an octave down (`\grace { d,16 e, }`) its beam stays inside the bass staff,
%% nothing reaches the gap, and LilyPond answered 9.000 on BOTH books — a pair measuring
%% nothing. The fixture's own pitches put that grace at D4/E4, above the bass staff, and the
%% difference appears (9.384 against 9.000). RULES 5.0: check that the two sides are the same
%% music before believing an identity.
\book {
  \probeG "GCN"
  \score {
    <<
      \new Staff { \clef "treble" \fixed c' { \time 4/4 \key c \major c'4 d' e' f' | g'1 | } }
      \new Staff { \clef "bass" \fixed c' {
        \time 4/4 \key c \major
        g,4 a, b, c |
        f2 g2 | } }
    >>
  }
}

%% MGN -- the control: the same book with NO grace.
\book {
  \probeG "MGN"
  \score {
    \new Staff \fixed c' {
      \time 4/4
      \key c \major
      \mark \markup { \box "A" } c'4 d' e' f' |
      g' a' b' c'' |
      c''4 b' a' g' |
      f' e' d' c' |
      \break
      \mark \markup { \box "B" } a'4 b' a' g' |
      f'4 e' d' c' |
    }
  }
}
