\version "2.26.0"

%% A CHORD DIAGRAM ON A GRACE NOTE (session 964).
%%
%% Lily#'s `@diagram(x32010)` is LilyPond's `\markup \fret-diagram-terse` TextScript, and Lily#
%% spaces diagrams side by side (the owner's rule, session 646 — LilyPond's \textLengthOn), so
%% every book here writes \textLengthOn, as the twin does. On a grace column the text stands
%% where it stands on a full-size one (its grid 1.689495 left of the column), and the column
%% is rodded against its neighbours by the box.
%%
%%   GDD  \textLengthOn c4 \grace { d8^\markup \fret-diagram-terse "x;3;2;o;1;o;" } e4 c2
%%   GDE  \textLengthOn c4 d8^\markup \fret-diagram-terse "x;3;2;o;1;o;" e8 c2      the full-size control
%%   GDF  \textLengthOn c4 \grace { d8 e8^\markup \fret-diagram-terse "x;3;2;o;1;o;" } f4 c2   on the second of two
%%
%% Read where the page put it: TextScript and NoteHead X extents.
%%
%% ⚠️ `\fixed c'` is LOAD-BEARING: Lily#'s absolute `c` is LilyPond's `c'` (RULES 6).
%% ⚠️ fonts.sans IS PINNED (as chord-symbol-width.ly pins it): the diagram's x and o are sans
%% text, and with -dbackend=svg LilyPond resolves the sans to the machine's generic face, which
%% widened the grid by 0.017 on the first run.

\paper { property-defaults.fonts.sans = "LilyPond Sans Serif" }

#(use-modules (srfi srfi-1))
#(define (dump-final tag)
   (lambda (layout pages)
     (for-each
      (lambda (page)
        (for-each
         (lambda (sys)
           (let ((sg (ly:prob-property sys 'system-grob)))
             (if (ly:grob? sg)
                 (let* ((all (ly:grob-array->list (ly:grob-object sg 'all-elements))))
                   (for-each
                    (lambda (g)
                      (let ((n (assq-ref (ly:grob-property g 'meta) 'name)))
                        (if (memq n '(TextScript NoteHead))
                            (let ((ext (ly:grob-extent g sg X)))
                              (format #t "\nPROBE ~a ~a x=~,6f..~,6f\n" tag n (car ext) (cdr ext))))))
                    all)))))
         (ly:prob-property page 'lines)))
      pages)))

\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GDD") }
  \score { \new Staff \fixed c' { \time 4/4 \textLengthOn c4 \grace { d8^\markup \fret-diagram-terse "x;3;2;o;1;o;" } e4 c2 \bar "|." } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GDE") }
  \score { \new Staff \fixed c' { \time 4/4 \textLengthOn c4 d8^\markup \fret-diagram-terse "x;3;2;o;1;o;" e8 c2 \bar "|." } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GDF") }
  \score { \new Staff \fixed c' { \time 4/4 \textLengthOn c4 \grace { d8 e8^\markup \fret-diagram-terse "x;3;2;o;1;o;" } f4 c2 \bar "|." } } }
