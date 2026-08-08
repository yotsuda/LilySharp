\version "2.26.0"
%% LP FIDELITY PROBE — the DRAWN tuplet bracket's SLOPE inside the staff: does the
%% bracket follow the note contour, or does the staff edge flatten it?
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe tuplet-bracket-sloped.ly (two books).
%%
%% WHAT IS BEING MEASURED, AND WHY THE CORPUS HAD NO POINT FOR IT
%%
%% calc_position_and_height's no-beam branch does NOT slope from the notes: its
%% graphical_dy is rv[dir] - lv[dir] where rv/lv are the BOUND columns' extents
%% UNITED WITH THE STAFF widened by staff-padding 0.25 (tuplet-bracket.cc:530-535,
%% rv.unite (staff)) — so for a tuplet whose bound stems end INSIDE the staff, the
%% staff edge 2.25 dominates both bounds and dy collapses to the small difference
%% of "staff edge vs the one stem that pokes past it" (≈0.25 here), then the
%% damping (:566-630) and the per-point offset pass (:708-719, every column extent
%% + the staff edge at x0/x1, then padding 1.1) put the LINE one padding past the
%% highest stem. Lily#'s CalculateSlope slopes from the outer MUSICAL positions
%% (g→c = 2.0 staff spaces here) — the DERIVED-NOT-TRANSCRIBED device its own
%% comment discloses as ⑴..⑶ and gates on exactly this pair (points before ports).
%%
%% THE FLOOR IS MADE TO BIND as tuplet-number-beamed.ly does, mirrored upward: the
%% tuplet staff sits BELOW a BASS staff of middle-line wholes (no downward
%% protrusion, staff ink 2.05 is the upper side), spacing loses basic/minimum
%% distance keeping padding 1 — the gap IS (bracket/number up-reach) + 2.05 + 1.
%% ⚠️ The upper staff MUST be bass: with treble over treble the CLEF PAIR binds
%% (upper G-tail ~3.08 down + lower G-top ~4.13 up + 1 = 8.210039) and the gap is
%% DEAF to the tuplet — measured 2026-08-09, the identical 8.210039 with the
%% tuplet REMOVED is the falsifier (the same trap tuplet-number-beamed.ly's
%% header documents, mirrored).
%%
%% THE BOOKS (one claim, one quantity; LP-IDENTITY pair):
%%   TBSD — c''2 \tuplet 3/2 { g'4 e' c' }: DESCENDING quarter triplet, stems up,
%%          bracket above. LP's bracket is nearly FLAT (dy ≈ -0.2, the staff-edge
%%          union); the LEFT end lands at 3.6 = the STAFF POINT at x1 (2.3 + 0.2)
%%          + 1.1 — decomposed six-digit 2026-08-09, see the ledger whys: the
%%          offset-pass point x is the column REFPOINT minus x0 (the bound STEM's
%%          face), so the left column's own x is NEGATIVE and never binds here.
%%   TBSA — c''2 \tuplet 3/2 { c'4 e' g' }: the ASCENDING mirror. Same extreme,
%%          same midpoint, same number ink by symmetry.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2, with signs):
%%   * LP IDENTITY: TBSD gap == TBSA gap to six digits — the binding ink is the
%%     NUMBER's top (midpoint 3.5 + half-ink 0.628 ≈ 4.13 beats the line end
%%     3.6 + half-thickness), and the midpoint is mirror-invariant. FALSIFIER: a
%%     six-digit split means the damping or the sign gates are NOT mirror-safe —
%%     record which side wins; the port must then reproduce the asymmetry.
%%   * TBSD positions ≈ (3.61 . 3.39) — dy ≈ -0.22, NOT -2.0. The gap reads
%%     ≈ 4.13 + 2.05 + 1 ≈ 7.18.
%%   * Lily# mirrors: BOTH books slope the full musical dy (±2.0) — the line's
%%     high end reaches 5.6 and BEATS its own number (centre 4.6 + 0.63 = 5.23),
%%     so the gap reads ≈ 5.68 + 2.05 + 1 ≈ 8.73, residual ≈ +1.5 on BOTH books
%%     (Lily# is its own mirror-identity; a split between the Lily# books would
%%     mean the slope application is direction-dependent, a second defect).
%%   * Each book: ONE system, TWO staves, TWO TupletBracket rows (+ numbers).
%%
%% The GROB rows also print pos=(l . r) — ly:grob-property 'positions, the raw
%% quantity calc_positions returns — so the port can be pinned against the
%% positions themselves, not only the compound gap.

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
                                        (format #t "PROBEV GROB ~a ~a name=~a rel=~a ext=(~a . ~a) x=(~a . ~a) pos=~a\n"
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

%% TBSD — descending quarter triplet inside the staff, bracket above: the staff-edge
%%     union flattens the bracket; the line sits at the g' stem + 1.1.
\book {
  \probeTag "TBSD"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff { \clef bass d1 | d1 | d1 \bar "|." }
      \new Staff { \repeat unfold 2 { c''2 \tuplet 3/2 { g'4 e' c' } } b'1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% TBSA — the ascending mirror: LP-identity by midpoint symmetry.
\book {
  \probeTag "TBSA"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff { \clef bass d1 | d1 | d1 \bar "|." }
      \new Staff { \repeat unfold 2 { c''2 \tuplet 3/2 { c'4 e' g' } } b'1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}
