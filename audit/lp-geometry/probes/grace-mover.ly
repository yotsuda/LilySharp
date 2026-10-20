\version "2.26.0"

%% A FERMATA OR AN ORNAMENT ON A GRACE NOTE (session 916, HANDOFF §2 U8 ⒟).
%%
%% Both sit ABOVE (direction UP), at general-grace-settings' Script font-size -3, over the
%% grace column. The fermata family is an outside-staff MOVER (outside-staff-priority 75,
%% scm/script.scm), so its height is read where the page has put it — after the collision pass
%% (page-post-process), not in after-line-breaking, which runs BEFORE the pass and reads the
%% aligned_side answer (0.40 over the staff where the pass pays 0.46: 2.526 against the final
%% 2.586 on the full-size control; measured session 916, Lab sessions/p916/u8/ferm.ly).
%%
%%   GMA  c'4 \grace { d'16\fermata } e'4 c'2     fermata over a grace inside the staff
%%   GMB  c'4 \grace { g'16\prall } e'4 c'2       a prall over a grace
%%   GMC  c'4 \grace { g'16\trill } e'4 c'2       a trill over a grace
%%   GMD  c'4 \grace { d'''16\fermata } e'4 c'2   fermata over a grace above the staff
%%   GME  c'4 d'16\fermata e'8. c'2              the full-size control of GMA
%%
%% Read as the ledger's other scripts are: the script's ORIGIN above the staff's middle line.
%%
%% Output (LilyPond 2.26.0):
%%   GMA 2.577882  GMB 2.603553  GMC 2.300000  GMD 7.567882  GME 2.586000
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

\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GMA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\fermata } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GMB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { g16\prall } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GMC") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { g16\trill } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GMD") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d''16\fermata } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GME") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16\fermata e8. c2 \bar "|." } } } }
