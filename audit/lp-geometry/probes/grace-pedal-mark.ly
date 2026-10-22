\version "2.26.0"

%% THE ORGAN'S HEEL AND TOE ON A GRACE NOTE (session 946, HANDOFF §2 U8).
%%
%% \heel and \toe are Scripts like the staccato (scm/script.scm heel: upedalheel both ways,
%% toe: dpedaltoe both ways, direction UP), set at general-grace-settings' Script font-size -3
%% over the grace column. Each grace row has its full-size control on the main grid.
%%
%%   GPMA  c'4 \grace { d'16\heel } e'4 c'2          GPMC  c'4 d'16\heel e'8. c'2    control
%%   GPMB  c'4 \grace { d'16_\heel } e'4 c'2         GPMD  c'4 d'16_\heel e'8. c'2   control
%%   GPME  c'4 \grace { d'16\toe } e'4 c'2           GPMG  c'4 d'16\toe e'8. c'2     control
%%   GPMF  c'4 \grace { d'16_\toe } e'4 c'2          GPMH  c'4 d'16_\toe e'8. c'2    control
%%   GPMI  c'4 \grace { a'16\heel } e'4 c'2          GPMK  c'4 a'16\heel e'8. c'2    control
%%   GPMJ  c'4 \grace { a'16_\toe } e'4 c'2          GPML  c'4 a'16_\toe e'8. c'2    control
%%
%% Read as the ledger's other scripts are: the script's ORIGIN above the staff's middle line,
%% after the collision pass (page-post-process).
%%
%% Output (LilyPond 2.26.0):
%%   GPMA 2.300000  GPMB -3.919960  GPMC 2.300000  GPMD -4.413000
%%   GPME 2.300000  GPMF -3.919960  GPMG 2.300000  GPMH -4.413000
%%   GPMI 2.500000  GPMJ -3.075901  GPMK 3.227917  GPML -3.418000
%%
%% ⚠️ `\fixed c'` is LOAD-BEARING: Lily#'s absolute `c` is LilyPond's `c'` (RULES 6).
#(use-modules (srfi srfi-1))

#(define (dump-final tag)
   (lambda (layout pages)
     (for-each
      (lambda (page)
        (for-each
         (lambda (sys)
           (let ((sg (ly:prob-property sys 'system-grob)))
             (if (ly:grob? sg)
                 (let* ((all (ly:grob-array->list (ly:grob-object sg 'all-elements)))
                        (staff (find (lambda (g) (eq? 'StaffSymbol (assq-ref (ly:grob-property g 'meta) 'name))) all)))
                   (for-each
                    (lambda (g)
                      (if (eq? 'Script (assq-ref (ly:grob-property g 'meta) 'name))
                          (format #t "\nPROBE ~a SCRIPT ~a y=~,6f\n" tag
                                  (cdr (ly:grob-property g 'script-stencil))
                                  (- (ly:grob-relative-coordinate g sg Y)
                                     (ly:grob-relative-coordinate staff sg Y)))))
                    all)))))
         (ly:prob-property page 'lines)))
      pages)))


\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GPMA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\heel } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GPMB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16_\heel } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GPMC") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16\heel e8. c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GPMD") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16_\heel e8. c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GPME") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\toe } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GPMF") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16_\toe } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GPMG") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16\toe e8. c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GPMH") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16_\toe e8. c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GPMI") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { a16\heel } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GPMJ") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { a16_\toe } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GPMK") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 a16\heel e8. c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GPML") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 a16_\toe e8. c2 \bar "|." } } } }