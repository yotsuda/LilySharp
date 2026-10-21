\version "2.26.0"

%% A TRILL SPANNER STARTING OR STOPPING ON A GRACE NOTE (session 924, HANDOFF §2 U8).
%%
%% Trill_spanner_engraver bounds its spanner at note columns, a grace's as any, and the
%% grace's column is one of its side supports: the grace's head and its stem (UP, whatever the
%% pitch) push the line.
%%
%%   TSA  c'4 \grace { d'16\startTrillSpan } e'4 f'4 g'4\stopTrillSpan     from a grace
%%   TSB  c'4 d'16\startTrillSpan e'8. f'4 g'4\stopTrillSpan               control
%%   TSC  c'4\startTrillSpan e'4 \grace { f'16\stopTrillSpan } g'4 a'4     stopping on a grace
%%   TSD  c'4 \grace { a''16\startTrillSpan } e'4 f'4 g'4\stopTrillSpan    from a high grace
%%   TSE  c'4 a''16\startTrillSpan e'8. f'4 g'4\stopTrillSpan              control (stem DOWN)
%%   TSF  c'4\startTrillSpan e'4 \grace { a''16\stopTrillSpan } g'4 a'4    stopping on a high grace
%%
%% Read where the page put it (page-post-process): the TrillSpanner's Y (its line) above
%% the staff's middle line.
%%
%% Output (LilyPond 2.26.0):
%%   TSA 3.550000  TSB 3.550000  TSC 3.550000  TSD 7.100000  TSE 5.045000  TSF 3.550000
%% TSD stands higher than its control because a grace stem is UP: the a'' grace's stem tip
%% binds, where the control's stem points down. TSF stays quiet: the line's drawn end stops
%% short of the bound grace (whole trill elements), so its column imposes nothing.
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
                        (if (memq nm '(TrillSpanner NoteHead))
                            (format #t "\nPROBE ~a ~a y=~,6f x=~,6f xext=~a yext=~a\n" tag nm
                                    (- (ly:grob-relative-coordinate g sg Y)
                                       (ly:grob-relative-coordinate staff sg Y))
                                    (ly:grob-relative-coordinate g sg X)
                                    (ly:grob-extent g sg X) (ly:grob-extent g sg Y)))))
                    all)))))
         (ly:prob-property page 'lines)))
      pages)))

\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "TSA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\startTrillSpan } e4 f4 g4\stopTrillSpan \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "TSB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16\startTrillSpan e8. f4 g4\stopTrillSpan \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "TSC") }
  \score { \new Staff { \time 4/4 \fixed c' { c4\startTrillSpan e4 \grace { f16\stopTrillSpan } g4 a4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "TSD") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { a'16\startTrillSpan } e4 f4 g4\stopTrillSpan \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "TSE") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 a'16\startTrillSpan e8. f4 g4\stopTrillSpan \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "TSF") }
  \score { \new Staff { \time 4/4 \fixed c' { c4\startTrillSpan e4 \grace { a'16\stopTrillSpan } g4 a4 \bar "|." } } } }
