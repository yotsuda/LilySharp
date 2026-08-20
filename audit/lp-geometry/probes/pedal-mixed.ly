\version "2.26.0"
%% LP FIDELITY PROBE — MIXED-STYLE SUSTAIN: "Ped." then a bracket line, one stretch.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Prefix PROBEM
%%
%% MEASURED (2026-08-20, 2.26.0): the SustainPedal word, the PianoPedalBracket and
%% their SustainPedalLineSpanner all dump relY -9.773 = 5.997000 below the staff
%% refpoint, in BOTH books (with and without the lyrics) -- one group, one Y, the same
%% 5.997 the text-style word reads on this music (the binding ink is the same Ped.
%% outline over the same note). The lyric line lands at 8.367115015225147 below the
%% refpoint = the bracket line's ink bottom 6.047 + relatedstaff padding 0.5 + its
%% ascender -- the same floor arithmetic as pedal-lyric-stack.ly PLB/PLT.
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
                          (if (memq nm '(SustainPedal PianoPedalBracket LyricText
                                         SustainPedalLineSpanner StaffSymbol))
                              (format #t "PROBEM ~a ~a relY=~a extY=(~a . ~a) relX=~a extX=(~a . ~a)\n"
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
probeM =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEM BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})
%% PMB — the PLB music at mixed style: Ped. text then the line, one stretch.
\book {
  \probeM "PMB"
  \score {
    \new Staff \with { pedalSustainStyle = #'mixed }
      { \time 4/4 c'4 d'\sustainOn e' f'\sustainOff | g'2 g' | }
  }
}
%% PMBL — the same with lyrics, the PLB lyric-floor twin at mixed style.
\book {
  \probeM "PMBL"
  \score {
    <<
      \new Staff \with { pedalSustainStyle = #'mixed }
        { \new Voice = "mel" { \time 4/4 c'4 d'\sustainOn e' f'\sustainOff | g'2 g' | } }
      \new Lyrics \lyricsto "mel" { la la la la ho ho }
    >>
  }
}
