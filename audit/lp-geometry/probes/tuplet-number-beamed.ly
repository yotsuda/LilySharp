\version "2.26.0"
%% LP FIDELITY PROBE — the NUMBER of a FULLY BEAMED tuplet as staff-to-staff binding ink.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe tuplet-number-beamed.ly (two tiny books).
%%
%% WHAT IS BEING MEASURED, AND WHY THE CORPUS HAD NO POINT FOR IT
%%
%% When every note of a tuplet is under one beam, LilyPond prints NO bracket
%% (bracket-visibility = if-no-beam) but the NUMBER still prints, riding the beam — and a
%% printed grob is in its staff's VerticalAxisGroup skyline like any other ink. Lily#'s
%% SkylineBuilder.AddTupletBracketsToSkyline instead SKIPS the whole tuplet when
%% !ShowBracket ("its number rides the beam"), i.e. it assumes the beam's own seed covers
%% the number. It does not: the number's ink stands proud of the beam on the outward side.
%% The stacker's above-staff seed already reserves the beamed number (99ecd3aa); this pair
%% is the point the STAFF-STAFF path needs before it can be given the same truth.
%%
%% THE FLOOR IS MADE TO BIND the same way system-clef-floor.ly does it: the two staves'
%% default-staff-staff-spacing loses its basic-distance 9 and minimum-distance 8, keeping
%% the shipping padding 1, so the gap the dump prints IS the skyline distance plus 1.
%%
%% THE PAIR: TNB engraves beat-long beamed TRIPLETS (number only, no bracket); TNC the
%% same pitches as plain beamed eighths — same heads, same down stems, same beam Y — so
%% LilyPond's difference between the two books is the tuplet number's own depth below the
%% beam and NOTHING else. The Lily# mirror can spell both (no \omit needed).
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2, with signs):
%%   * TNB reads MORE gap than TNC — sign certain. Magnitude: measured on the FIRST cut
%%     of this probe (treble/treble, c''/d'' — which never entered the regime, see the
%%     trap below), LilyPond puts the beamed number's CENTRE at the INVISIBLE bracket's
%%     position — beam lower edge + TupletBracket padding 1.1, NOT riding on the beam —
%%     so the step is (1.100 + half the number's ink 0.628 − nothing of the beam) ≈ +1.7.
%%   * FALSIFIER: TNB == TNC means the number is NOT in LilyPond's staff skyline either,
%%     the whole port premise is wrong, and Lily#'s skip must be KEPT.
%%   * Both books put ONE system with TWO staves on one page; if the staff count or the
%%     system count differs the books are not a pair.
%%
%% ⚠️ TRAP, hit on the first cut of this probe (HANDOFF 5.0 "確かめてから信じる"): with
%% treble clefs on BOTH staves the pointwise clef-against-clef term is 7.210039 (the
%% skyline-binding.ly number) and it beat the number's reach by 0.002322 — both books
%% read the identical 8.210039 and measured NOTHING about tuplets. Hence the paper below:
%% the LOWER staff takes a BASS clef (small up-reach, so the clef pair drops to ~4.6+1)
%% and the triplet sits a half-step lower (b'/c'', beam lower edge ≈ 2.9 below the
%% refpoint), so the beam term (≈ 5.95) out-reaches the clef pair in the CONTROL and the
%% number term (≈ 7.7) in TNB. FALSIFIER: either book reading ≈ the clef-pair value
%% (~5.6) is the same trap again — treat the entry as unmeasured, do not record it.
%%
%% ⚠️ The tuplet number is serif ITALIC TEXT, and it is the binding ink here — the serif
%% pin is load-bearing (svg backend resolves fonts.serif via this machine's fontconfig
%% otherwise; page-vertical.ly's header has the history).

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
                   ;; The TupletNumber rides along so the reading can be decomposed: rel is
                   ;; its centre about the SYSTEM refpoint, ext its own ink — together they
                   ;; say how far past the beam the number actually reaches.
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

%% TNB — beat-long beamed TRIPLETS: no bracket, the number rides the beam, and with the
%%     spacing zeroed the gap reads (beam+number skyline against the lower staff) + 1.
\book {
  \probeTag "TNB"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff { \repeat unfold 8 { \tuplet 3/2 { b'8[ c'' b'] } } b'1 \bar "|." }
      \new Staff { \clef bass d1 | d1 | d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% TNC — THE CONTROL: the same heads under the same manual beams with no tuplet at all,
%%     so the gap is the BEAM's own depth + 1 and the difference TNB − TNC is the number.
\book {
  \probeTag "TNC"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff { \repeat unfold 4 { b'8[ c'' b' c''] } b'1 \bar "|." }
      \new Staff { \clef bass d1 | d1 | d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}
