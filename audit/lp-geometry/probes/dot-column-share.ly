\version "2.26.0"
%% LP FIDELITY PROBE: the voices of a staff share one dot column at a moment.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe dot-column-share.ly (three books). Reads the left
%% edge of every Dots grob of the first moment; the spread is the largest less the smallest.
%%
%% Dot_column_engraver is a Staff engraver, so a staff has one DotColumn a moment, translated by
%% the configuration's x_offset (lily/dot-column.cc:229-232) whichever voices its dots come from.
%% Lily# solved a rest's dots apart from the notes' until session 1101 and stood them up to 1.66 away.
%%
%% THE BOOKS:
%%   DSN, THE CONTROL: two dotted notes, whose own answers already agree.
%%   DSR: a dotted rest beside a dotted note, whose own answers differ by the head and the rest.
%%   DSF: a dotted note whose flag pushes its dot, beside one with no flag to do so.
%%
%% PREDICTIONS, written before running: a spread of 0 in all three. FORK: a spread in DSR or DSF
%% means dots do not share the column's X, and the shift Lily# stamps would be wrong.

#(define (dump-dots layout pages)
   (for-each
    (lambda (page)
      (for-each
       (lambda (sys)
         (let ((sg (ly:prob-property sys 'system-grob)))
           (if (ly:grob? sg)
               (for-each
                (lambda (g)
                  (if (eq? (assq-ref (ly:grob-property g 'meta) 'name) 'Dots)
                      (format #t "\nPROBEV DOT x=~a pos=~a"
                              (car (ly:grob-extent g sg X)) (ly:grob-staff-position g))))
                (ly:grob-array->list (ly:grob-object sg 'all-elements))))))
       (ly:prob-property page 'lines)))
    pages))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEV BOOK ~a\n" tag)
                                      (dump-dots layout pages)) } #})

\book {
  \probeTag "DSN"
  \score { \new Staff << { e''4. c''8 c''2 } \\ { d'4. c'8 c'2 } >> }
}

\book {
  \probeTag "DSR"
  \score { \new Staff << { f''4. c''8 c''2 } \\ { r4. c'8 c'2 } >> }
}

\book {
  \probeTag "DSF"
  \score { \new Staff << { g'8. r16 g'4 g'2 } \\ { c'8. c'16 c'4 c'2 } >> }
}
