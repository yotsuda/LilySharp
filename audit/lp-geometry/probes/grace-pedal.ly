\version "2.26.0"

%% A PEDAL BRACKET ENGAGED OR RELEASED ON A GRACE NOTE (session 922, HANDOFF §2 U8 ⒟).
%%
%% Piano_pedal_engraver bounds its bracket at note columns, a grace's as any, and the grace
%% column is one of the bracket's supports: its SMALL head sets the line's height.
%%
%%   GPA  c'4 \grace { d'16\sustainOn } e'4 f'4 g'4\sustainOff   engaged on a grace
%%   GPB  c'4 d'16\sustainOn e'8. f'4 g'4\sustainOff             the full-size control
%%   GPC  c'4\sustainOn e'4 \grace { f'16\sustainOff } g'4 a'4   released on a grace
%%
%% Read where the page put it (page-post-process): the PianoPedalBracket's X-extent and Y
%% (its line, under the staff's middle line), the NoteHeads' X.
%%
%% Output (LilyPond 2.26.0), X relative to the system:
%%   GPA grace head 10.986796, g head 18.929913, bracket (10.936796 . 18.979913), y -5.144060
%%       = the grace head's ink bottom 2.894060 + padding 1.2 + hook 1.05.
%%   GPB d head 13.4892, g head 25.303755, bracket (13.4392 . 25.353755), y -5.295000.
%%   GPC c head 8.585, grace head 14.239041, bracket (8.535 . 14.289041), y -5.795000.
%% The X-extent is the line's ends widened by the half pen 0.05 (lily/line-interface.cc).
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
                        (if (memq nm '(PianoPedalBracket NoteHead))
                            (format #t "\nPROBE ~a ~a y=~,6f x=~,6f xext=~a yext=~a\n" tag nm
                                    (- (ly:grob-relative-coordinate g sg Y)
                                       (ly:grob-relative-coordinate staff sg Y))
                                    (ly:grob-relative-coordinate g sg X)
                                    (ly:grob-extent g sg X) (ly:grob-extent g sg Y)))))
                    all)))))
         (ly:prob-property page 'lines)))
      pages)))

ped = { \set Staff.pedalSustainStyle = #'bracket }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GPA") }
  \score { \new Staff { \time 4/4 \ped \fixed c' { c4 \grace { d16\sustainOn } e4 f4 g4\sustainOff \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GPB") }
  \score { \new Staff { \time 4/4 \ped \fixed c' { c4 d16\sustainOn e8. f4 g4\sustainOff \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GPC") }
  \score { \new Staff { \time 4/4 \ped \fixed c' { c4\sustainOn e4 \grace { f16\sustainOff } g4 a4 \bar "|." } } } }
