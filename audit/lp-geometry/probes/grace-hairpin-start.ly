\version "2.26.0"

%% A HAIRPIN OPENING ON A GRACE NOTE (session 918, HANDOFF §2 U8 ⒟).
%%
%% Dynamic_engraver hands the hairpin the grace's NoteColumn as its LEFT bound, as it does any
%% column; a dynamic on the same grace is the bound instead (Text_interface: its right edge
%% plus bound-padding 1.0). The wedge is side-positioned off the grace column's own head and
%% UP stem like any support.
%%
%%   GHA  c'4 \grace { d'16\< } e'4 f'4 g'4\!        a crescendo from a grace
%%   GHB  c'4 \grace { d'16\p\< } e'4 f'4 g'4\!      ... from a dynamic on the grace
%%   GHC  c'4 \grace { d'16\> } e'4 f'4 g'4\!        a decrescendo from a grace
%%   GHD  c'4 d'16\< e'8. f'4 g'4\!                  the full-size control of GHA
%%   GHE  c'4 \grace { a'16\< b'16 } e'4 f'4 g'4\!   from a BEAMED grace
%%
%% Read where the page put it (page-post-process): the Hairpin's centre below the staff's
%% middle line and its X-extent (the arms start 0.05 inside it: half the 0.1 line), the
%% NoteHeads' X and the DynamicText's extent.
%%
%% Output (LilyPond 2.26.0): Hairpin y / xext.left + 0.05 - grace head x
%%   GHA -3.613088 / 0.000000   GHB -4.062091 / 2.193050   GHC -4.207094
%%   GHD -3.763290 / 0.000000   GHE -3.456979 / 0.000000
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
                        (if (memq nm '(Hairpin NoteHead DynamicText))
                            (format #t "\nPROBE ~a ~a y=~,6f x=~,6f xext=~a\n" tag nm
                                    (- (ly:grob-relative-coordinate g sg Y)
                                       (ly:grob-relative-coordinate staff sg Y))
                                    (ly:grob-relative-coordinate g sg X)
                                    (ly:grob-extent g sg X)))))
                    all)))))
         (ly:prob-property page 'lines)))
      pages)))

\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GHA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\< } e4 f4 g4\! \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GHB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\p\< } e4 f4 g4\! \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GHC") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\> } e4 f4 g4\! \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GHD") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16\< e8. f4 g4\! \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GHE") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { a16\< b16 } e4 f4 g4\! \bar "|." } } } }
