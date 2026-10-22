\version "2.26.0"

%% A FIGURED BASS GROUP ON A GRACE NOTE (session 964).
%%
%% LilyPond's Figured_bass_engraver stands a figure event's BassFigureAlignment on the grace's
%% paper column — at the grace head's X — and the column's spacing does not move for it (the
%% figure is narrower than the grace spring).
%%
%%   GFA  c4 \grace { d8 } e4 c2 (bass clef) with \figuremode { <5>4 \grace { <6>8 } <3>4 s2 }
%%   GFB  the same staff with <5>4 <3>4 s2                       the control: no figure on the grace
%%   GFF  c4 \grace { d8 e8 } f4 c2 with <5>8. s16 \grace { s8 <6>8 } <3>4 s2   two graces, the second figured
%%
%% ⚠️ In GFA LilyPond ALSO prints the 5 over the 6 at the grace: the <5>4 is still sounding at
%% the grace's moment (its end is the main note's moment, and the grace comes before it). The
%% twin exporter says so in a warning; Lily#'s figures carry no duration and print the 6 alone.
%%
%% Read where the page put it: BassFigure and NoteHead X extents (the figure's X is read off the
%% FIRST figure of each column; the stacked 5 in GFA shares it).
%%
%% ⚠️ `\fixed c` with `\clef bass`: Lily#'s absolute `c,` is LilyPond's `c` (RULES 6).

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
                        (if (memq n '(BassFigure NoteHead))
                            (let ((ext (ly:grob-extent g sg X)))
                              (format #t "\nPROBE ~a ~a x=~,6f..~,6f\n" tag n (car ext) (cdr ext))))))
                    all)))))
         (ly:prob-property page 'lines)))
      pages)))

\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GFA") }
  \score { << \new Staff \fixed c { \clef bass \time 4/4 c4 \grace { d8 } e4 c2 \bar "|." }
              \new FiguredBass \figuremode { <5>4 \grace { <6>8 } <3>4 s2 } >> } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GFB") }
  \score { << \new Staff \fixed c { \clef bass \time 4/4 c4 \grace { d8 } e4 c2 \bar "|." }
              \new FiguredBass \figuremode { <5>4 <3>4 s2 } >> } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GFF") }
  \score { << \new Staff \fixed c { \clef bass \time 4/4 c4 \grace { d8 e8 } f4 c2 \bar "|." }
              \new FiguredBass \figuremode { <5>8. s16 \grace { s8 <6>8 } <3>4 s2 } >> } }
