\version "2.26.0"

%% THE THUMB IN ITS NOTE'S SCRIPT COLUMN (session 949, HANDOFF §2 U8).
%%
%% \thumb is a Fingering (ly/script-init.ly): New_fingering_engraver gives it script-priority
%% 100 + its head's position, so it stacks between a flageolet (50) / accent (0) / staccato
%% (-100) below and a trill (150) / fermata (175) / bow (180) above. It clears what is under it
%% by its padding 0.5 and no horizon-padding, and its PROFILE is its extent box (Fingering
%% declares no vertical-skylines) -- what is over it reads that box.
%%
%%   THSA  c'4 d'16\thumb\upbow e'8. c'2         THSJ  c'4 c''4\thumb\accent e'4 c'4
%%   THSB  c'4 d'16\thumb\fermata e'8. c'2       THSK  c'4 d'16\thumb\flageolet e'8. c'2
%%   THSG  c'4 \grace { d'16\thumb\upbow } e'4 c'2
%%   THSI  c'4 d'''16\thumb\staccato e'8. c'2    THSM  c'4 d'4\thumb\upbow\downbow e'4 c'4
%%
%% Read: each Fingering's and Script's ORIGIN above the staff's middle line, after the
%% collision pass (page-post-process).
%%
%% Output (LilyPond 2.26.0):
%%   THSA thumb 3.350000 upbow 4.050178     THSB fermata 4.386178   THSG upbow 3.669365
%%   THSI thumb 6.945000                    THSJ thumb 4.320671 accent 2.670000
%%   THSK thumb 4.350000                    THSM downbow 6.330178
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
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "THSA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16\thumb\upbow e8. c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "THSB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16\thumb\fermata e8. c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "THSG") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\thumb\upbow } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "THSI") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d''16\thumb\staccato e8. c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "THSJ") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 c'4\thumb\accent e4 c4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "THSK") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16\thumb\flageolet e8. c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "THSM") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d4\thumb\upbow\downbow e4 c4 \bar "|." } } } }