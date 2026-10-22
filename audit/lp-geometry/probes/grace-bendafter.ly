\version "2.26.0"

%% A FALL OR DOIT ON A GRACE NOTE (session 947, HANDOFF §2 U8).
%%
%% \bendAfter is Bend_engraver's BendAfter (scm/scheme-engravers.scm:2096-2165): the left bound
%% the note head, the right the next musical column; bend::print (scm/output-lib.scm) leaves the
%% head's (or its row's dot's) ink right by 0.5 and ends 0.5 short of the next column's ink, at
%% least minimum-length 0.5 on. general-grace-settings names no BendAfter: full size on a grace.
%%
%%   GFAA  c'4 \grace { d'16\bendAfter #-4 } e'4 c'2           GFAF  c'4 d'16\bendAfter #-4 e'8. c'2  control
%%   GFAB  c'4 \grace { d'16\bendAfter #+4 e'16 } f'4 c'2      GFAI  c'4 \grace { d'16 e'16\bendAfter #-4 } f'4 c'2
%%   GFAD  c'4 \grace { d'8.\bendAfter #-4 e'16 } f'4 c'2      GFAJ  c'4 d'4\bendAfter #-4 \grace { e'16 } f'4 c'4
%%   GFAE  c'4 \grace { d'16\bendAfter #-4 } fis'4 c'2         GFAK  c'4 d'4\bendAfter #-4 f'4 c'4    control
%%
%% Read: each BendAfter's and NoteHead's X extent in the system; the ledger takes the curve's
%% start minus its head's left, and its reach.
%%
%% Output (LilyPond 2.26.0), start - head left / reach:
%%   GFAA 1.417939 / 0.5   GFAB 1.417939 / 0.5   GFAD 2.035232   GFAE reach 1.570688
%%   GFAF 1.8042           GFAI 1.417938         GFAJ reach 0.5  GFAK reach 0.948045
%%
%% ⚠️ A CHORD draws NO BendAfter, on a grace or not (Lab sessions/p947/bendch.ly): the second
%% head's acknowledgement stops the first's fall at its own column. Lily# still draws one.
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
                      (let ((n (assq-ref (ly:grob-property g 'meta) 'name)))
                        (if (memq n '(BendAfter NoteHead Dots BarLine Accidental))
                            (let ((ext (ly:grob-extent g sg X)) (yext (ly:grob-extent g sg Y)))
                              (format #t "\nPROBE ~a ~a ~a x=~,6f..~,6f y=~,6f..~,6f oy=~,6f\n" tag n
                                      ""
                                      (car ext) (cdr ext)
                                      (- (car yext) (ly:grob-relative-coordinate staff sg Y))
                                      (- (cdr yext) (ly:grob-relative-coordinate staff sg Y))
                                      (- (ly:grob-relative-coordinate g sg Y) (ly:grob-relative-coordinate staff sg Y)))))))
                    all)))))
         (ly:prob-property page 'lines)))
      pages)))
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GFAA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\bendAfter #-4 } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GFAB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\bendAfter #+4 e16 } f4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GFAD") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d8.\bendAfter #-4 e16 } f4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GFAE") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\bendAfter #-4 } fis4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GFAF") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16\bendAfter #-4 e8. c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GFAI") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 e16\bendAfter #-4 } f4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GFAJ") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d4\bendAfter #-4 \grace { e16 } f4 c4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GFAK") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d4\bendAfter #-4 f4 c4 \bar "|." } } } }