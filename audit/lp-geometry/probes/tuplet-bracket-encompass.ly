\version "2.26.0"
%% LP FIDELITY PROBE — the DRAWN tuplet bracket's encompass points: are they the REAL
%% stem tips (quanted beam face / shortened stem), or a raw default stem length?
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe tuplet-bracket-encompass.ly (three books).
%%
%% WHAT IS BEING MEASURED, AND WHY THE CORPUS HAD NO POINT FOR IT
%%
%% LilyPond has no beamed-specific tuplet formula: calc_position_and_height reads every
%% note column's ACTUAL extent (Note_column::cross_staff_extent) — a beamed stem ends at
%% the quanted beam face, an unbeamed one at its real (possibly shortened) tip — then
%% puts the bracket line one TupletBracket padding 1.1 beyond the extreme, and the number
%% rides the line's midpoint. Lily#'s TupletBracketEngraver has TWO branches: the beamed
%% branch (ShowBracket=false) reads the quanted beam edge + 1.1 (ported session 30), but
%% the DRAWN-bracket branch (CalculateSlope/OutwardTip) still builds its encompass from
%% the raw DefaultStemLength 3.5 — it never sees the beam model or the stem shortening.
%% HANDOFF ▶ⓐ names this pair as the port's observation surface: a PARTIAL beam with the
%% bracket shown, spanning a beam.
%%
%% THE FLOOR IS MADE TO BIND exactly as tuplet-number-beamed.ly does it: the two staves'
%% default-staff-staff-spacing loses basic-distance and minimum-distance, keeping the
%% shipping padding 1, so the gap the dump prints IS the skyline distance plus 1. The
%% lower staff is BASS (small up-reach; the treble/treble clef-pair trap is documented in
%% tuplet-number-beamed.ly's header) and the bracketed music sits at b'/c'' so the
%% bracket term out-reaches the upper staff's own clef down-reach 3.540.
%%
%% THE BOOKS (outer pitches equal in every tuplet => FLAT bracket, slope stays out of
%% the reading; one claim, one quantity):
%%   TPB — \tuplet 3/2 { c''8[ b'] c'' }: first two eighths under a manual beam, third
%%         flagged => bracket-visibility if-no-beam PRINTS the bracket. The encompass
%%         extreme should be the quanted BEAM FACE at the b' stem.
%%   TPC — the same 2-note c''[ b'] beams with no tuplet at all: the falsifier's
%%         baseline. EXPECTED to read the clef term 6.590000 like TNC (the bare beam
%%         ~3.2-3.3 loses to the clef 3.540) — its job is the identity check, not a
%%         beam reading.
%%   TPS — \tuplet 3/2 { b'4 c'' b' }: no beam anywhere; the encompass extreme is the
%%         real QUARTER stem tip of the middle-line b', which stem.cc SHORTENS
%%         (dir*hp[dir] >= 0 includes the middle line; 3.5 - 1/6 = 10/3).
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2, with signs):
%%   * TPB: bracket line rel sits at (beam face) + 1.100 SIX-DIGIT — read the Beam row's
%%     own face from the same dump and subtract. FALSIFIER FORK: if line - beamFace is
%%     not 1.100 but line - (flagged c'' tip ~3.0 below middle) is, the flag column is
%%     the extreme instead — the port claim (real column extents) is unchanged, only the
%%     anchor differs; record which. If the line sits at raw 3.5 + 1.1 the whole ⓐ
%%     premise is wrong and Lily#'s fallback is already LilyPond's shape.
%%   * TPB gap > TPC gap — sign certain (the number's ink bottom ~ face + 1.1 + 0.628
%%     against the control's clef 3.540).
%%   * TPS: bracket line rel = 10/3 + 1.1 = 4.433333 below the middle line. FALSIFIER:
%%     line at 3.5 + 1.1 = 4.600 means the middle-line quarter is NOT shortened at the
%%     bracket's encompass and Lily#'s raw 3.5 is right in this regime.
%%   * Lily# mirrors (prediction recorded in the ledger whys): TPB and TPS read
%%     NINE-DIGIT IDENTICAL (both flat brackets from the same raw 3.5 extreme over a b'
%%     head) — LilyPond separates the two books, Lily# structurally cannot; that
%%     identity, not the residual, is the defect (HANDOFF 5.3 「同じであってはならない
%%     数が同じ」). Residuals ≈ +(3.5 − beamFace) on TPB and +1/6 on TPS.
%%   * Every book: ONE system, TWO staves. TPB must show 8 Beam rows and 8 TupletBracket
%%     rows; TPC 8 Beam rows and NO TupletBracket; TPS no Beam and 4 TupletBracket rows.
%%     A missing/extra beam means the autobeamer fought the manual beams — treat the
%%     book as unmeasured, do not record it.
%%
%% ⚠️ The tuplet number is serif ITALIC TEXT and rides the bracket midpoint deeper than
%% the line itself (half-ink 0.628 below) — the serif pin is load-bearing.

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
                   ;; TupletBracket / TupletNumber / Beam ride along so the reading can
                   ;; be decomposed: rel is the grob about the SYSTEM refpoint, ext its
                   ;; own ink — together they say where the bracket line sits and how
                   ;; far past it the number reaches.
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (or (eq? nm 'TupletNumber) (eq? nm 'TupletBracket)
                                            (eq? nm 'Beam))
                                        (format #t "PROBEV GROB ~a ~a name=~a rel=~a ext=(~a . ~a) x=(~a . ~a)\n"
                                                n i nm
                                                (ly:grob-relative-coordinate g sg Y)
                                                (car (ly:grob-extent g g Y))
                                                (cdr (ly:grob-extent g g Y))
                                                (+ (ly:grob-relative-coordinate g sg X)
                                                   (car (ly:grob-extent g g X)))
                                                (+ (ly:grob-relative-coordinate g sg X)
                                                   (cdr (ly:grob-extent g g X)))))))
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

%% TPB — partial beam, bracket SHOWN and flat: the encompass extreme is the quanted beam
%%     face at the b' stem, and the gap reads (bracket line + number reach) + 1.
\book {
  \probeTag "TPB"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff { \repeat unfold 8 { \tuplet 3/2 { c''8[ b'] c'' } } b'1 \bar "|." }
      \new Staff { \clef bass d1 | d1 | d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% TPC — THE CONTROL: the same 2-note beams with no tuplet at all. Expected clef-bound
%%     at 6.590000 (TNC's shape); its job is the identity falsifier.
\book {
  \probeTag "TPC"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff { \repeat unfold 8 { c''8[ b'] } b'1 \bar "|." }
      \new Staff { \clef bass d1 | d1 | d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% TPS — quarter triplets, no beam: the encompass extreme is the real (shortened)
%%     quarter stem of the middle-line b'.
\book {
  \probeTag "TPS"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff { \repeat unfold 4 { \tuplet 3/2 { b'4 c'' b' } } b'1 \bar "|." }
      \new Staff { \clef bass d1 | d1 | d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}
