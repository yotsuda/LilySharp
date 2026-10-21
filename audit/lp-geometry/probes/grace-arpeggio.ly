\version "2.26.0"

%% AN ARPEGGIO ON A GRACE CHORD (session 919, HANDOFF §2 U8 ⒟).
%%
%% Arpeggio_engraver makes an ordinary Arpeggio (or, under \nonArpeggiato, a ChordBracket)
%% on a grace chord. general-grace-settings names neither, so it keeps its FULL size, and it
%% stands its padding 0.5 left of the grace column's heads and accidentals; the grace column
%% is spaced to make room for it.
%%
%%   GAA  c'4 \grace { <d' f' a'>16\arpeggio } e'4 c'2          a wiggle on a grace chord
%%   GAB  c'4 <d' f' a'>16\arpeggio e'8. c'2                    the full-size control
%%   GAC  c'4 \grace { <d' fis' a'>16\arpeggio } e'4 c'2        ... clearing a grace sharp
%%   GAD  c'4 \grace { <d' f' a'>16 } e'4 c'2                   no arpeggio: where the grace stands
%%   GAE  c'4 \grace { <d' f' a'>16-\nonArpeggiato } e'4 c'2     the bracket (ChordBracket)
%%   GAF  c'4 \grace { <d' f' a'>16\arpeggio <e' g' b'>16 } e'4 c'2     on the first of two graces
%%
%% Read where the page put it (page-post-process): the X- and Y-extents of the Arpeggio /
%% ChordBracket, the NoteHeads and the Accidental.
%%
%% Output (LilyPond 2.26.0), X relative to the system:
%%   c' head 8.585.  GAA wiggle (10.1892 . 10.9892) height 3.0, grace heads 11.4892 (GAD 10.986796).
%%   GAB wiggle (12.1892 . 12.9892), head 13.4892.  GAC wiggle as GAA, sharp ink left 11.4892,
%%   heads 12.532157.  GAE bracket (10.1892 . 10.6892) height 3.5, heads 11.1892.  GAF as GAA.
%%
%% ⚠️ `\fixed c'` is LOAD-BEARING: Lily#'s absolute `c` is LilyPond's `c'` (RULES 6).

#(define (dump-final tag)
   (lambda (layout pages)
     (for-each
      (lambda (page)
        (for-each
         (lambda (sys)
           (let ((sg (ly:prob-property sys 'system-grob)))
             (if (ly:grob? sg)
                 (let* ((all (ly:grob-array->list (ly:grob-object sg 'all-elements)))
                        (staff (car (filter (lambda (g) (eq? 'StaffSymbol (assq-ref (ly:grob-property g 'meta) 'name))) all))))
                   (for-each
                    (lambda (g)
                      (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                        (if (memq nm '(Arpeggio ChordBracket NoteHead Accidental))
                            (format #t "\nPROBE ~a ~a y=~,6f x=~,6f xext=~a yext=~a\n" tag nm
                                    (- (ly:grob-relative-coordinate g sg Y)
                                       (ly:grob-relative-coordinate staff sg Y))
                                    (ly:grob-relative-coordinate g sg X)
                                    (ly:grob-extent g sg X) (ly:grob-extent g sg Y)))))
                    all)))))
         (ly:prob-property page 'lines)))
      pages)))

\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GAA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { <d f a>16\arpeggio } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GAB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 <d f a>16\arpeggio e8. c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GAC") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { <d fis a>16\arpeggio } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GAD") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { <d f a>16 } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GAE") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { <d f a>16-\nonArpeggiato } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GAF") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { <d f a>16\arpeggio <e g b>16 } e4 c2 \bar "|." } } } }
