\version "2.26.0"

%% A BOWING MARK OR ANOTHER UP-SCRIPT ON A GRACE NOTE (session 923, HANDOFF §2 U8).
%%
%% \upbow \downbow \flageolet \stopped \snappizzicato \thumb are Scripts like the staccato
%% (scm/script.scm), set at general-grace-settings' Script font-size -3 over the grace column.
%%
%%   GBA  c'4 \grace { d'16\upbow } e'4 c'2           GBE  ... \snappizzicato
%%   GBB  c'4 \grace { d'16\downbow } e'4 c'2         GBF  ... \thumb (no Script is printed)
%%   GBC  c'4 \grace { d'16\flageolet } e'4 c'2       GBG  c'4 d'16\upbow e'8. c'2      control
%%   GBD  c'4 \grace { d'16\stopped } e'4 c'2         GBH  c'4 d'16\flageolet e'8. c'2  control
%%
%% Read as the ledger's other scripts are: the script's ORIGIN above the staff's middle line,
%% after the collision pass (page-post-process).
%%
%% Output (LilyPond 2.26.0):
%%   GBA 2.300000  GBB 2.300000  GBC 2.532843  GBD 2.637495  GBE 2.626181  GBF (no Script)
%%   GBG 2.300000  GBH 2.650000
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

\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\upbow } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\downbow } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBC") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\flageolet } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBD") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\stopped } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBE") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\snappizzicato } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBF") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\thumb } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBG") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16\upbow e8. c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBH") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16\flageolet e8. c2 \bar "|." } } } }
