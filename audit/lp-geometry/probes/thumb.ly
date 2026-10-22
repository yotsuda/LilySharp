\version "2.26.0"

%% THE THUMB POSITION (session 948, HANDOFF §2 U8).
%%
%% \thumb is \finger \markup \scale #(cons (magstep 5) (magstep 5)) \musicglyph "scripts.thumb"
%% (ly/script-init.ly:74-75): a FINGERING, not a Script. A lone fingering goes UP whatever the
%% stem (fingeringOrientations '(up down)), its ink 0.5 (padding) clear of the staff's ink, the
%% heads' ink and a beamed stem (add-stem-support only-if-beamed). The magstep 5 undoes the
%% Fingering's font-size -5: full size on a note, the grace Script's -3 on a grace (Fingering -8).
%%
%%   THMA  c'4 d'16\thumb e'8. c'2              THMF  c'4 d'''16\thumb e'8. c'2
%%   THMB  c'4 c''16\thumb e'8. c'2  (beamed)   THMG  c'4 d'16\thumb e'16 e'8 c'2
%%   THMC  c'4 d'16_\thumb e'8. c'2             THMH  c'4 d'16\thumb\staccato e'8. c'2
%%   THMD  c'4 \grace { d'16\thumb } e'4 c'2    THMI  c'4 \grace { c''16_\thumb } e'4 c'2
%%   THME  c'4 \grace { c''16\thumb } e'4 c'2
%%
%% Read: the Fingering's ORIGIN (relative Y) above the staff's middle line, after the collision
%% pass; its ink is -0.8 .. 0.500178 about it at full size.
%%
%% Output (LilyPond 2.26.0):
%%   THMA 3.350000  THMB 4.507557  THMC -4.045178  THMD 3.115685  THME 3.115685
%%   THMF 6.345000  THMG 3.350000  THMH 3.350000   THMI -2.903679
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
                      (let ((n (assq-ref (ly:grob-property g 'meta) 'name)))
                        (if (memq n '(Script NoteHead Fingering))
                            (let ((ext (ly:grob-extent g sg X)) (yext (ly:grob-extent g sg Y)))
                              (format #t "\nPROBE ~a ~a ~a x=~,6f..~,6f y=~,6f..~,6f oy=~,6f\n" tag n
                                      (if (eq? n 'Script) (cdr (ly:grob-property g 'script-stencil)) "")
                                      (car ext) (cdr ext)
                                      (- (car yext) (ly:grob-relative-coordinate staff sg Y))
                                      (- (cdr yext) (ly:grob-relative-coordinate staff sg Y))
                                      (- (ly:grob-relative-coordinate g sg Y) (ly:grob-relative-coordinate staff sg Y)))))))
                    all)))))
         (ly:prob-property page 'lines)))
      pages)))
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "THMA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16\thumb e8. c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "THMB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 c'16\thumb e8. c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "THMC") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16_\thumb e8. c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "THMD") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\thumb } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "THME") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { c'16\thumb } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "THMF") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d''16\thumb e8. c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "THMG") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16\thumb e16 e8 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "THMH") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16\thumb\staccato e8. c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "THMI") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { c'16_\thumb } e4 c2 \bar "|." } } } }