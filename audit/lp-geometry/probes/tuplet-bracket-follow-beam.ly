\version "2.26.0"
%% LP FIDELITY PROBE — the tuplet bracket that FOLLOWS ITS BEAM: what are its two
%% encompass points, and does the general arm's flattening reach it?
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe tuplet-bracket-follow-beam.ly (three books).
%%
%% WHAT IS BEING MEASURED, AND WHY THE CORPUS HAD NO POINT FOR IT
%%
%% calc_position_and_height has TWO arms and they are not one arm with a flag. The
%% FOLLOW-BEAM arm (tuplet-bracket.cc:495-519) takes the outer two COLUMNS' stem tips,
%% sets dy to their difference, pushes exactly those two points, and stops. Everything
%% else — uniting with the staff, the musical sign gates, the damping, the per-column
%% points — is in the ELSE arm (:520-631), and :633-637 pushes the staff edge only
%% `if (!follow_beam)'. par_beam itself is scm/output-lib.scm:3945-3968
%% ly:tuplet-bracket::calc-potential-beam: the tuplet's FIRST and LAST columns' stems,
%% both carrying one beam.
%%
%% ⚠️ THE LEDGER HAD NO POINT IN THIS REGIME AND THAT IS WHY IT DRIFTED. The three
%% tuplet-bracket pairs already here measure the ELSE arm only: tuplet-bracket-sloped
%% (TBSD/TBSA) has no beam at all, and tuplet-bracket-encompass's TPB is a FLAT beam,
%% where the follow arm and the general arm cannot be told apart. Session 343 corrected
%% three faults inside the follow arm and moved 18 of 920 swept books with NOTHING here
%% watching; these are that debt paid (HANDOFF 5.0: points before ports).
%%
%% THE FLOOR IS MADE TO BIND exactly as tuplet-bracket-encompass.ly does it: the tuplet
%% staff sits ABOVE a BASS staff of middle-line wholes (no upward protrusion, so the
%% lower side of the gap is that staff's ink 2.05), the bracketed music sits just above
%% the middle line so its stems point DOWN and the bracket hangs INTO the gap, and
%% default-staff-staff-spacing loses basic-distance and minimum-distance keeping the
%% shipping padding 1 — the gap IS (bracket/number down-reach) + 2.05 + 1.
%%
%% ⚠️ THE FIRST DRAFT OF THIS PROBE WAS DEAF, and its own falsifier said so (2026-09-07).
%% It put the tuplet on the LOWER staff with the bracket ABOVE — mirroring
%% tuplet-bracket-sloped.ly — but a follow-beam bracket sits only one padding off the
%% BEAM, which is much nearer the staff than an unbeamed stem tip, so it reached 2.94
%% where that staff's own TREBLE CLEF reaches ~4.13 up: all three books printed the
%% identical staff-to-staff 6.826. Hanging it DOWNWARD clears the treble clef's tail
%% (~3.08) instead, which is the arrangement TPB already uses and measures at 4.963.
%%
%% THE BOOKS (the beam runs from OUTSIDE the tuplet, so the bracket is DRAWN — a beam
%% with the tuplet's own bounds would be `equally_long' and hide it — and the notes
%% DESCEND so the beam is SLOPED, which is what separates the two arms):
%%   TFB — b'2 e''8[ \tuplet 3/2 { d''8 c'' b' ] } b'8. Both of the tuplet's columns are
%%         on the beam, so par_beam is set and the bracket follows it. The two encompass
%%         points are the d'' and b' STEMS' tips.
%%   TFR — the same with the tuplet's FIRST column a REST the beam runs over:
%%         e''8[ \tuplet 3/2 { r8 c'' b' ] }. Every LilyPond note column has a stem, a
%%         rest's included, and a beamed rest's is one of the beam's own (measured
%%         2026-09-07, scratch/p345/beamrest.ly) — so par_beam is STILL set and the left
%%         encompass point is that INVISIBLE stem, standing on the rest's ink centre.
%%   TFC — THE FALSIFIER: the same four notes beamed with no tuplet at all. Its job is
%%         to show the gap is bracket-bound rather than clef- or beam-bound.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2, with signs):
%%   * TFC < TFB by a clear margin (a whole bracket + number). If TFC == TFB the book is
%%     deaf and nothing below means anything — record it and redesign, do not record a
%%     number. (It did, once; see the note above.)
%%   * TFB's positions are SLOPED, not flat: the follow arm never unites with the staff,
%%     so the ±0.2 collapse that TBSD/TBSA measure must NOT appear here. FALSIFIER: a
%%     dy of ≈0.2 would mean par_beam is not being set on this shape and the book is
%%     measuring the general arm after all — check the beam= row before recording.
%%   * TFR vs TFB: the beam descends and the stems point DOWN, so its LEFT end is the
%%     shallow end; moving the left encompass point from d'' back to the rest's stem
%%     takes it further left and therefore LESS far down, so the bracket's dy grows while
%%     its deepest point (the right end) stays put. Sign: TFR ≈ TFB at the binding end,
%%     and the two separate in `pos' rather than in the gap. Record both.
%%   * Lily# mirrors: session 343 separated the two arms, took the follow arm's points at
%%     the outer COLUMNS (the bounding rest's invisible stem included) and stopped both
%%     beam lookups from reaching another staff. So all three books should read CLOSE
%%     here — this pair is not opened to expose a defect but to hold a corrected regime
%%     that had nothing watching it. A large residual means the correction is wrong in a
%%     way the two twins it was checked against (scratch/p345 e1 / multistaff) did not
%%     separate.
%%
%% The GROB rows print pos=(l . r) — ly:grob-property 'positions, the raw quantity
%% calc_positions returns — and beam=SET/#f, so the port can be pinned against the
%% positions themselves and against par_beam, not only the compound gap.

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
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (or (eq? nm 'TupletNumber) (eq? nm 'TupletBracket))
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

%% TFB — the bracket follows a SLOPED beam that starts outside it.
\book {
  \probeTag "TFB"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff {
        \repeat unfold 2 { b'2 e''8[ \tuplet 3/2 { d''8 c'' b' ] } b'8 }
        b'1 \bar "|."
      }
      \new Staff { \clef bass d1 | d1 | d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% TFR — the same, with the tuplet's FIRST column a rest the beam runs over.
\book {
  \probeTag "TFR"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff {
        \repeat unfold 2 { b'2 e''8[ \tuplet 3/2 { r8 c'' b' ] } b'8 }
        b'1 \bar "|."
      }
      \new Staff { \clef bass d1 | d1 | d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% TFC — THE FALSIFIER: the same four notes, no tuplet, so no bracket and no number.
\book {
  \probeTag "TFC"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff {
        \repeat unfold 2 { b'2 e''8[ d''8 c'' b' ] }
        b'1 \bar "|."
      }
      \new Staff { \clef bass d1 | d1 | d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}
