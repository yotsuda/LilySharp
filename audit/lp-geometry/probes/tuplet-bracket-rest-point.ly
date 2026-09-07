\version "2.26.0"
%% LP FIDELITY PROBE — the DRAWN (general-arm) tuplet bracket's encompass points: does a
%% REST column push a point, and does it do so even at a bound where it is no slope bound?
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe tuplet-bracket-rest-point.ly (four books).
%%
%% WHAT IS BEING MEASURED, AND WHY THE CORPUS HAD NO POINT FOR IT
%%
%% calc_position_and_height's general (else) arm walks EVERY note column of the tuplet raw
%% (lily/tuplet-bracket.cc:554-562) and pushes each column's cross_staff_extent[dir] as an
%% encompass point — a rest lives in a NoteColumn like a note, so a rest's ink is a point.
%% Only the SLOPE bounds skip rests (:523-528 get_bounds, "outer non-rest columns"). Lily#'s
%% TupletBracketEngraver has walked the notes and chords only (its `pos == null` gate),
%% which session 343 disclosed on 2026-09-07 while porting the arm split: "LilyPond pushes
%% the REST columns' own ink as points; Lily# still pushes note/chord columns only. Harmless
%% on that book — a default mid-staff rest's bottom (-1.25) loses to the staff edge (-2.30)
%% — but a rest written at a pitch (`a4\rest', Lily# `a,4@rest') or pushed out of the staff
%% can be the extreme." No pinned point measured that regime. These are its points.
%%
%% THE FLOOR IS MADE TO BIND exactly as tuplet-bracket-encompass.ly and
%% tuplet-bracket-follow-beam.ly do it: the tuplet staff sits ABOVE a BASS staff of
%% middle-line wholes (no upward protrusion, so the lower side of the gap is that staff's
%% ink 2.05), the bracketed music is c'' — one step above the middle line, stems DOWN, so
%% the bracket hangs INTO the gap — and default-staff-staff-spacing loses basic-distance
%% and minimum-distance keeping the shipping padding 1: the gap IS (bracket/number
%% down-reach) + 2.05 + 1.
%%
%% THE BOOKS (the outer notes are the same pitch in every tuplet, so every bracket is FLAT
%% and only the DEPTH is in play — one claim, one quantity):
%%   TQC — \tuplet 3/2 { c''4 r4 c'' } c''2: THE CONTROL. The middle rest sits on the
%%         middle line, ink (-1.25 . 1.5624), which loses to both the staff edge -2.30 and
%%         the c'' stem tips at -3.0 (position 1, down, 3.5 unshortened). The bracket
%%         clears the tips: positions (-4.1 . -4.1).
%%   TQD — the same with the rest WRITTEN at c' (`c'4\rest', staff-position -6, centre
%%         -3.0): its ink bottom -3.0 - 1.25 = -4.25 is now the extreme. The bracket
%%         clears the REST: positions (-5.35 . -5.35).
%%   TQU — the same with the rest written at a'' (`a''4\rest', +3.0): its ink is entirely
%%         on the OTHER side of the bracket's, so note_ext[DOWN] of that column is above
%%         the tips and nothing moves. LP-IDENTITY pair with TQC: the point is direction
%%         aware (:561 note_ext[dir]), not "a rest anywhere".
%%   TQB — the deep rest FIRST: \tuplet 3/2 { c'4\rest c''4 c'' }. get_bounds skips it for
%%         the slope (the bounds are the two c''), but the points loop does not skip it.
%%         LP-IDENTITY pair with TQD in depth: the bracket is as deep as TQD's although the
%%         rest is a bound; only its x-span differs (x0 is now the rest's ink edge).
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2, with signs):
%%   * TQC positions = (-4.1 . -4.1) six-digit, beam=#f. TQU's TupletBracket rows and the
%%     staff-to-staff gap are SIX-DIGIT IDENTICAL to TQC's (LP identity).
%%   * TQD positions = (-5.35 . -5.35): deeper than TQC by exactly the rest's reach past
%%     the tips, 4.25 - 3.0 = 1.250000. The gap grows by the same 1.250000. FALSIFIER: a
%%     TQD gap equal to TQC's means LilyPond does NOT push rest columns and the disclosed
%%     gap in Lily# is not a gap at all; record it and do not port.
%%   * TQB's gap and positions equal TQD's six-digit (LP identity in depth). FALSIFIER:
%%     TQB shallower than TQD means a BOUND rest is skipped as a point too, i.e. the
%%     points loop and get_bounds share a filter this reading of :554-562 does not see.
%%   * Every book prints Rest rows; TQD/TQB's deep rest reads ext (-1.25 . 1.5624) about
%%     rel = -3.0 (the rest's ink is what Lily#'s GlyphMetrics.RestQuarter says it is).
%%   * Lily# mirrors (prediction recorded in the ledger whys): TQC and TQU EXACT (the flat
%%     general arm is pinned six-digit by TBSD/TBSA and TPS); TQD and TQB both at
%%     -1.250000 — Lily# leaves the bracket at the tips, 1.25 SHALLOWER than LilyPond's
%%     (residual = lilysharp - lilypond is NEGATIVE). The two residuals are identical
%%     because Lily# skips the rest at a bound and in the middle alike.
%%   * Every book: ONE system, TWO staves, 2 TupletBracket rows, no Beam.
%%
%% ⚠️ The tuplet number is serif ITALIC TEXT and rides the bracket midpoint deeper than
%% the line itself — the serif pin is load-bearing (as in tuplet-bracket-encompass.ly).

#(define (probe-dump-pages layout pages)
   (format #t "\nPROBEV PAPER top-margin=~a paper-height=~a line-width=~a\n"
           (ly:output-def-lookup layout 'top-margin)
           (ly:output-def-lookup layout 'paper-height)
           (ly:output-def-lookup layout 'line-width))
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (format #t "PROBEV PAGE ~a systems=~a\n" n (length lines))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let* ((sys (car ls))
                        (ext (ly:stencil-extent (ly:prob-property sys 'stencil) Y))
                        (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0))))
                   (format #t "PROBEV SYS ~a ~a y=~a ext=(~a . ~a) staff=(~a . ~a)\n"
                           n i
                           (ly:prob-property sys 'Y-offset 0.0)
                           (car ext) (cdr ext)
                           (car staff) (cdr staff))
                   ;; TupletBracket / TupletNumber / Rest / Beam ride along so the reading
                   ;; can be decomposed: rel is the grob about the SYSTEM refpoint, ext its
                   ;; own ink — the Rest rows say where the rest's ink actually is, the
                   ;; bracket rows carry the raw positions and whether par_beam is set.
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (or (eq? nm 'TupletNumber) (eq? nm 'TupletBracket)
                                            (eq? nm 'Rest) (eq? nm 'Beam))
                                        (format #t "PROBEV GROB ~a ~a name=~a rel=~a ext=(~a . ~a) x=(~a . ~a) pos=~a beam=~a\n"
                                                n i nm
                                                (ly:grob-relative-coordinate g sg Y)
                                                (car (ly:grob-extent g g Y))
                                                (cdr (ly:grob-extent g g Y))
                                                (+ (ly:grob-relative-coordinate g sg X)
                                                   (car (ly:grob-extent g g X)))
                                                (+ (ly:grob-relative-coordinate g sg X)
                                                   (cdr (ly:grob-extent g g X)))
                                                (if (eq? nm 'TupletBracket)
                                                    (ly:grob-property g 'positions)
                                                    '())
                                                (if (eq? nm 'TupletBracket)
                                                    (if (ly:grob? (ly:grob-object g 'beam #f)) "SET" "#f")
                                                    '())))))
                                (ly:grob-array->list all))))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEV BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

zeroStaffStaff = \layout {
  \context {
    \Staff
    \override VerticalAxisGroup.default-staff-staff-spacing =
      #'((basic-distance . 0) (minimum-distance . 0) (padding . 1))
  }
}

%% TQC — THE CONTROL: the middle rest on the middle line loses to the stem tips.
\book {
  \probeTag "TQC"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff {
        \repeat unfold 2 { \tuplet 3/2 { c''4 r4 c'' } c''2 }
        c''1 \bar "|."
      }
      \new Staff { \clef bass d1 | d1 | d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% TQD — the middle rest written DEEP (c'): its ink is the extreme.
\book {
  \probeTag "TQD"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff {
        \repeat unfold 2 { \tuplet 3/2 { c''4 c'4\rest c'' } c''2 }
        c''1 \bar "|."
      }
      \new Staff { \clef bass d1 | d1 | d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% TQU — the middle rest written HIGH (a''): on the wrong side, so nothing moves.
\book {
  \probeTag "TQU"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff {
        \repeat unfold 2 { \tuplet 3/2 { c''4 a''4\rest c'' } c''2 }
        c''1 \bar "|."
      }
      \new Staff { \clef bass d1 | d1 | d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% TQB — the deep rest at the LEFT BOUND: no slope bound, still a point.
\book {
  \probeTag "TQB"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff {
        \repeat unfold 2 { \tuplet 3/2 { c'4\rest c''4 c'' } c''2 }
        c''1 \bar "|."
      }
      \new Staff { \clef bass d1 | d1 | d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}
