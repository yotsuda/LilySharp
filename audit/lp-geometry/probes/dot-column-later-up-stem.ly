\version "2.26.0"
%% LP FIDELITY PROBE: the dots of an up voice clear the stems of the later up voices.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe dot-column-later-up-stem.ly -Prefix PROBEX (six books).
%% The quantity is the leftmost dot's left edge less the leftmost head's; DLR's is the step to the
%% next column's head.
%%
%% lily/note-collision.cc:586-594 automatic_shift hands the dot column of an up voice the stem of
%% every later up voice, dotted or not. Only there is another voice's stem a support of the column.
%% Skyline::height reads a row at a building's edge from the first building whose end reaches it
%% (lily/skyline.cc:658-665), so a stem's box owns its top edge and not its bottom.
%%
%% THE BOOKS:
%%   DLA: e'' dotted over a voice-three c'', whose stem covers the dot's row 3.
%%   DLE: over a voice-three e', whose stem's box ends exactly on row 3.
%%   DLB: THE CONTROL of DLE: over d', one position lower, the box ends on row 2.
%%   DLD: THE CONTROL of direction: the dotted voice is the second, a down voice, under a voice three.
%%   DLG: THE CONTROL of order: the dotted voice is the third, the undotted the first.
%%   DLR: c''' dotted, a dotted rest in voice two, e'' eighths in voice three: the shared dots stand
%%        pushed, and the next head must not read the rest's dot on row 1.
%%   DLU: the first voice alone has dots, the other two none at all.
%%   DLV: THE CONTROL of direction on the row: a down voice's dots on row 3, a later up stem over it.
%%   DLW: a dot wiped by the merge of two same notes: the first up voice's column has no dot of its own.
%%   DLQ: as DLR over e'' and an a' eighth: the rest's dots leave the line, row -4 to row -3, and meet it.
%%   DLF: THE CONTROL of the bottom edge: a voice-three e'' whose stem's box begins on the dot's row 3.
%%
%% PREDICTIONS, written before running: DLA and DLE 2.406 (the voice's shift 0.652, the head 1.304,
%% the padding 0.45), DLB 1.754 (the head's own), DLD and DLG no later up stem on the row, and DLR's
%% step 3.16. FORK: DLE at 1.754 would mean the top edge is open.

#(define (probe-dump-pages layout pages)
   (for-each
    (lambda (page)
      (for-each
       (lambda (sys)
         (let ((sg (ly:prob-property sys 'system-grob)))
           (if (ly:grob? sg)
               (for-each
                (lambda (g)
                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                    (if (memq nm '(NoteHead Dots))
                        (format #t "PROBEX GROB name=~a left=~a pos=~a\n" nm
                                (car (ly:grob-extent g sg X)) (ly:grob-staff-position g)))))
                (ly:grob-array->list (ly:grob-object sg 'all-elements))))))
       (ly:prob-property page 'lines)))
    pages))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEX BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

\book {
  \probeTag "DLA"
  \score { \new Staff << { e''4. c''8 c''2 } \\ { r4. c'8 c'2 } \\ { c''4 c''4 c''2 } >> }
}

\book {
  \probeTag "DLE"
  \score { \new Staff << { e''4. c''8 c''2 } \\ { s4. c'8 c'2 } \\ { e'4 c''8 c''8 c''2 } >> }
}

\book {
  \probeTag "DLB"
  \score { \new Staff << { e''4. c''8 c''2 } \\ { s4. c'8 c'2 } \\ { d'4 c''8 c''8 c''2 } >> }
}

\book {
  \probeTag "DLD"
  \score { \new Staff << { c'''4 c''4 c''2 } \\ { e'4. c'8 c'2 } \\ { c'''4 c''4 c''2 } \\ { c'4 c'4 c'2 } >> }
}

\book {
  \probeTag "DLG"
  \score { \new Staff << { c''4 c''4 c''2 } \\ { r4. c'8 c'2 } \\ { e''4. c''8 c''2 } >> }
}

\book {
  \probeTag "DLU"
  \score { \new Staff << { e''4. c''8 c''2 } \\ { c'4 c'4 c'2 } \\ { c''4 c''4 c''2 } >> }
}

\book {
  \probeTag "DLV"
  \score { \new Staff << { g''4 c''4 c''2 } \\ { e''4. c'8 c'2 } \\ { c''4 c''4 c''2 } >> }
}

\book {
  \probeTag "DLW"
  \score { \new Staff << { e''4. c''8 c''2 } \\ { e''4. c'8 c'2 } \\ { c''4 c''4 c''2 } >> }
}

\book {
  \probeTag "DLQ"
  \score { \new Staff << { e''4. c''8 c''2 } \\ { r4. c'8 c'2 } \\ { a'8 a'8 c''4 c''2 } >> }
}

\book {
  \probeTag "DLF"
  \score { \new Staff << { e''4. c''8 c''2 } \\ { r4. c'8 c'2 } \\ { e''4 c''4 c''2 } >> }
}

\book {
  \probeTag "DLR"
  \score { \new Staff << { c'''4. c''8 c''2 } \\ { r4. c'8 c'2 } \\ { e''8 e''8 c''4 c''2 } >> }
}
