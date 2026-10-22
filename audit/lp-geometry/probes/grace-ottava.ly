\version "2.26.0"

%% AN OTTAVA STARTING OR STOPPING ON A GRACE NOTE (session 925, HANDOFF §2 U8).
%%
%% Ottava_spanner_engraver bounds its bracket at note columns, a grace's as any, and the
%% grace column is one of its side supports (the grace stem is UP). \ottava #0 written in a
%% grace body ends the span BEFORE the grace: the right bound is the last column the span
%% covered, and the grace is drawn at its written octave.
%%
%%   OGA  c'''4 \grace { \ottava #1 d'''16 } e'''4 f'''4 \ottava #0 g''4    from a grace
%%   OGB  c'''4 \ottava #1 d'''16 e'''8. f'''4 \ottava #0 g''4              control
%%   OGC  \ottava #1 c'''4 e'''4 \grace { \ottava #0 f''16 } g''4 a''4      stopping on a grace
%%   OGD  \ottava #1 c'''4 e'''4 \ottava #0 f''16 g''8. a''4                control
%%
%% Read where the page put it (page-post-process): the OttavaBracket's X-extent and Y, the
%% NoteHeads' X.
%%
%% Output (LilyPond 2.26.0), X relative to the system:
%%   OGA grace head 10.702729, bracket (9.902729 . 17.597801), y 5.320638
%%   OGB d head 13.2392,       bracket (12.4392 . 22.103755),  y 4.05
%%   OGC e head 11.837245 .. 13.141445, bracket right 13.791445, y 4.05
%%   OGD e head 13.7392 .. 15.0434,     bracket right 15.6934,   y 4.05
%% The left edge is the bound head less shorten-pair 0.8 in both; OGA stands higher because
%% the grace's UP stem is a support. The right edge (OGC, OGD) is the last covered head's
%% right + 0.6 + the hook's half pen 0.05, the same with a grace after it.
%%
%% More books, for the HEIGHT (session 925): what lifts OGA is NOT the grace.
%%   OGE  c''4 \grace { \ottava #1 d'''16 } ...   a low first note           y 4.15
%%   OGF  c'''4 r4 \grace { \ottava #1 d'''16 } ... a rest between           y 4.15
%%   OGG  c''4 \grace { \ottava #1 g''16 } ...    a grace whose stem is low  y 4.05
%%   OGH  c''4 \grace { \ottava #1 a''''16 } ...  a very high grace          y 9.65
%%   OGI  c''4 \ottava #1 d'''16 e'''8. ...      full-size control of OGE   y 4.05
%%   OGJ  c''4 \ottava #1 \stemUp d'''16 ...     full size, stem forced UP  y 4.25
%% OGA's 5.320638 is its first note c''' (head top 4.545), standing 0.0135 left of the
%% bracket. OGE's 0.1 over the floor is the grace's UP stem (tip 3.60) under the dashed
%% LINE (+ padding 0.5 + half pen) -- under LilyPond's default label "8", which is SHORT.
%% (OGJ's d16 is beamed to the e8., and LilyPond warns about the beam: no point reads it.)
%%
%% The same books with the label spelled "8va" as Lily# writes it (session 926):
%%   VGA  as OGA   y 5.320638     VGE  as OGE   y 4.887332     VGJ  as OGJ   y 4.908984
%% Under "8va" the grace's stem stands under the LABEL in both engines (VGE).
%% VGA's SPACING (session 952): c''' head to grace head 2.117729, grace head to e''' head
%% 1.938627 -- the grace spaced as the d'' it draws, the same as an unbracketed d'' grace
%% (Lab sessions/p952/no.ly). VGA's 5.320638 is the bracket lifted off the c''''s LEDGER LINE,
%% which runs 0.326 past the head under the label's left end (Lab sessions/p951).
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
                        (if (memq nm '(OttavaBracket NoteHead Stem Flag))
                            (format #t "\nPROBE ~a ~a y=~,6f x=~,6f xext=~a yext=~a\n" tag nm
                                    (- (ly:grob-relative-coordinate g sg Y)
                                       (ly:grob-relative-coordinate staff sg Y))
                                    (ly:grob-relative-coordinate g sg X)
                                    (ly:grob-extent g sg X) (ly:grob-extent g sg Y)))))
                    all)))))
         (ly:prob-property page 'lines)))
      pages)))

\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "OGA") }
  \score { \new Staff { \time 4/4 \fixed c' { c''4 \grace { \ottava #1 d''16 } e''4 f''4 \ottava #0 g'4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "OGB") }
  \score { \new Staff { \time 4/4 \fixed c' { c''4 \ottava #1 d''16 e''8. f''4 \ottava #0 g'4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "OGC") }
  \score { \new Staff { \time 4/4 \fixed c' { \ottava #1 c''4 e''4 \grace { \ottava #0 f'16 } g'4 a'4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "OGD") }
  \score { \new Staff { \time 4/4 \fixed c' { \ottava #1 c''4 e''4 \ottava #0 f'16 g'8. a'4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "OGE") }
  \score { \new Staff { \time 4/4 \fixed c' { c'4 \grace { \ottava #1 d''16 } e''4 f''4 \ottava #0 g'4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "OGF") }
  \score { \new Staff { \time 4/4 \fixed c' { c''4 r4 \grace { \ottava #1 d''16 } e''4 \ottava #0 g'4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "OGG") }
  \score { \new Staff { \time 4/4 \fixed c' { c'4 \grace { \ottava #1 g'16 } e''4 f''4 \ottava #0 g'4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "OGH") }
  \score { \new Staff { \time 4/4 \fixed c' { c'4 \grace { \ottava #1 a'''16 } e''4 f''4 \ottava #0 g'4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "OGI") }
  \score { \new Staff { \time 4/4 \fixed c' { c'4 \ottava #1 d''16 e''8. f''4 \ottava #0 g'4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "OGJ") }
  \score { \new Staff { \time 4/4 \fixed c' { c'4 \ottava #1 \stemUp d''16 \stemNeutral e''8. f''4 \ottava #0 g'4 \bar "|." } } } }
o = { \set Staff.ottavationMarkups = #ottavation-simple-ordinals }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "VGA") }
  \score { \new Staff { \time 4/4 \o \fixed c' { c''4 \grace { \ottava #1 d''16 } e''4 f''4 \ottava #0 g'4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "VGE") }
  \score { \new Staff { \time 4/4 \o \fixed c' { c'4 \grace { \ottava #1 d''16 } e''4 f''4 \ottava #0 g'4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "VGJ") }
  \score { \new Staff { \time 4/4 \o \fixed c' { c'4 \ottava #1 \stemUp d''16 \stemNeutral e''8. f''4 \ottava #0 g'4 \bar "|." } } } }
