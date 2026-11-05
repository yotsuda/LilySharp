\version "2.26.0"
%% LP FIDELITY PROBE: a voice that rests still wishes, so it halves the other voice's stem correction.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe rest-voice-wish.ly -Prefix PROBEX (five books).
%% The quantity is the step between the upper voice's head anchors.
%%
%% Note_spacing::stem_dir_correction returns at the first column with no stem
%% (lily/note-spacing.cc:248-249), but get_spacing still returns the wish, and merge_springs
%% averages every wish (lily/spacing-spanner.cc:364-393). Lily# skipped a rest's wish in that
%% average until session 1102 and charged the other voice's correction whole.
%%
%% THE BOOKS:
%%   RWA: quarters falling f'' to c'', up stems, over a voice of rests on the staff.
%%   RWS: the same over rests on another staff: a wish is the voice's, not the staff's.
%%   RWN: THE CONTROL, the same over a spacer, which makes no column and so wishes nothing.
%%   RWB: the last column into the bar line, a voice of rests under up stems.
%%   RWR: THE CONTROL of the bar line, the same over a spacer.
%%
%% PREDICTIONS, written before running: RWN's step is RWA's less 0.125, half the 0.25 of
%% same-direction-correction, and RWS equals RWA. RWB differs from RWR by half of the stem's
%% correction into the bar. FORK: RWA equal to RWN would mean a rest's voice does not wish.
%%
%% MEASURED, 2.26.0: RWA 2.700145 and RWN 2.752245, a difference of 0.0521, NOT the 0.125 predicted:
%% the prediction forgot that the rest's wish also reads the rest's narrower width (note-spacing.cc:77).
%% The two parts: (3.002245 + 2.648045) / 2 = 2.825145, less half the 0.25 = 2.700145. The fork did not
%% happen; RWS equals RWA. RWB's step into the bar is 4.962067, RWR's 5.186088.

#(define (probe-dump-pages layout pages)
   (for-each
    (lambda (page)
      (for-each
       (lambda (sys)
         (let ((sg (ly:prob-property sys 'system-grob)))
           (if (ly:grob? sg)
               (for-each
                (lambda (g)
                  (if (eq? (assq-ref (ly:grob-property g 'meta) 'name) 'NoteHead)
                      (format #t "PROBEX GROB name=NoteHead anchor=~a\n"
                              (ly:grob-relative-coordinate g sg X))))
                (ly:grob-array->list (ly:grob-object sg 'all-elements))))))
       (ly:prob-property page 'lines)))
    pages))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEX BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

\book {
  \probeTag "RWA"
  \paper { ragged-right = ##t indent = 0 }
  \score { \new Staff << { f''4 c'' f'' c'' | } \\ { r4 r r r | } >> }
}

\book {
  \probeTag "RWS"
  \paper { ragged-right = ##t indent = 0 }
  \score { << \new Staff { f''4 c'' f'' c'' | } \new Staff { \clef bass r4 r r r | } >> }
}

\book {
  \probeTag "RWN"
  \paper { ragged-right = ##t indent = 0 }
  \score { \new Staff << { f''4 c'' f'' c'' | } \\ { s4 s s s | } >> }
}

\book {
  \probeTag "RWB"
  \paper { ragged-right = ##t indent = 0 }
  \score { \new Staff { << { c''4 c'' c'' c'' | } \\ { r4 r r r | } >> c''1 | } }
}

\book {
  \probeTag "RWR"
  \paper { ragged-right = ##t indent = 0 }
  \score { \new Staff { << { c''4 c'' c'' c'' | } \\ { s4 s s s | } >> c''1 | } }
}
