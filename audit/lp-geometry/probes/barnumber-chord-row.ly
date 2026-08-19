\version "2.26.0"
%% LP FIDELITY PROBE — WHERE A CONTINUATION BAR NUMBER SITS WHEN A CHORDNAMES LINE
%% LEADS THE SYSTEM.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe barnumber-chord-row.ly -Prefix PROBEC
%%
%% THE DEFECT THIS MEASURES (user report, session 220): on a lead sheet — a chords row
%% above the melody — Lily#'s continuation bar numbers ride ABOVE the whole chord band,
%% about 2.5 ss higher than LilyPond puts them. BarNumberLayout.YUp is anchored on the
%% SYSTEM TOP, and with a leading row the system top is the band's top, not the staff's.
%%
%% LILYPOND'S MECHANISM: BarNumber carries after-line-breaking =
%% ly:side-position-interface::move-to-extremal-staff (define-grobs.scm:320-321), which
%% walks the alignment's elements from the top and re-parents the number onto the first
%% LIVE element whose X-extent intersects the number's own X widened by 1.0
%% (side-position-interface.cc:510-563, staff-grouper-interface.cc get_extremal_staff).
%% A ChordNames line's ink starts at the first chord — past the clef — while a line-start
%% number hangs INTO the left margin (X (-0.956 . 0.0)), so the two are X-disjoint and
%% the number stays on the STAFF, tucked BELOW the chord row.
%%
%% MEASURED (2026-08-20, 2.26.0), system 2 (number "6") of this book:
%%   StaffSymbol rel=-1.938700 ext=(-2.05 . 2.05)     -> staff refpoint at -1.938700
%%   ChordName   rel= 3.106300 ext=(-0.060 . 1.939)   -> chords 5.045 ABOVE the refpoint
%%   BarNumber   rel= 1.131773 ext=(-0.020473 . 1.156)-> baseline 3.070473 above refpoint
%% i.e. the number's INK BOTTOM = 3.070473 - 0.020473 = 3.050000 exactly
%% (staff line ink 2.05 + BarNumber padding 1.0) — the same 3.05 the plain books
%% BNL/BNH pinned, UNMOVED by the chord line above. The ledger point reads the ink
%% bottom rather than the baseline so neither side carries a digit's overshoot and the
%% two engines' line breaking is free to pick different numerals.

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
                          (if (memq nm '(BarNumber StaffSymbol ChordName))
                              (format #t "PROBEC ~a ~a rel=~a ext=(~a . ~a)\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))
probeC =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEC BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})
\book {
  \probeC "BNC"
  \score {
    <<
      \new ChordNames \chordmode { c1 g a:m f c g a:m f c g a:m f }
      \new Staff \relative c'' {
        \time 4/4
        c4 d e f | g a b c | c4 b a g | f e d c |
        c4 d e f | \break g a b c | c4 b a g | f e d c | \break
        c4 d e f | g a b c | c4 b a g | f e d c |
      }
    >>
  }
}
