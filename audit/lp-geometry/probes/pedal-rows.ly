\version "2.26.0"
%% LP FIDELITY PROBE — WHERE TEXT-STYLE PEDAL WORDS SIT OVER A PLAIN STAFF, ALL THREE
%% FAMILIES STRUCK TOGETHER.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe pedal-rows.ly -Prefix PROBER
%%
%% The dump twin of pedal-three.ly's fourth score, with two changes that make it a
%% LEDGER book rather than an attribution book: the grob rows are DUMPED (9 digits,
%% not read off the drawn SVG), and a SPACER bar separates the engage and release
%% columns so no word overlaps a neighbour's ink in EITHER engine — Lily#'s default
%% page is narrower than this probe family's line-width 60, and without the spacer its
%% release words collide with the engage words and stack, measuring the collision pass
%% where the point wants the quiet rows.
%%
%% THE MECHANISM (lily/piano-pedal-align-engraver.cc): each pedal ITEM gets a
%% LineSpanner of its own — is_finished() ends the spanner at the first timestep
%% without a pedal item of that family — so every word side-positions independently:
%% quiet = its support + padding 1.2 (SustainPedalLineSpanner), then the outside-staff
%% collision pass at 0.46 stacks only words whose INK overlaps in X. Same-column
%% families stack nearest-first una corda, sostenuto, sustain (pedal-three.ly).
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
                          (if (memq nm '(SustainPedal SostenutoPedal UnaCordaPedal
                                         StaffSymbol))
                              (format #t "PROBER ~a ~a relY=~a extY=(~a . ~a) relX=~a\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))
                                      (ly:grob-relative-coordinate g sg X)))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))
probeR =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBER BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})
\book {
  \probeR "P3S"
  \score {
    \new Staff \with { pedalSustainStyle = #'text pedalSostenutoStyle = #'text }
      { \clef bass c1\sustainOn\sostenutoOn\unaCorda | c1 |
        c1\sustainOff\sostenutoOff\treCorde | }
  }
}
