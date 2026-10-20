\version "2.26.0"

%% A TEXT ON A GRACE NOTE (session 916, HANDOFF §2 U8 ⒟).
%%
%% The ordinary Text_engraver makes a TextScript on a grace note: full size (general-grace-settings
%% names no TextScript), its ink LEFT on the grace column's reference point (TextScript aligns
%% neither on itself nor on its parent), sided off the staff.
%%
%%   GXA  c'4 \grace { d'16_"dolce" } e'4 c'2     text below a grace (the default side)
%%   GXB  c'4 \grace { d'16^"dolce" } e'4 c'2     text above a grace
%%   GXC  c'4 d'16_"dolce" e'8. c'2               the full-size control of GXA
%%
%% Read where the page put it (page-post-process): the baseline above the staff's middle line,
%% and its X against the grace head's left edge.
%%
%% Output (LilyPond 2.26.0): see the PROBE lines.
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
                        (if (memq nm '(TextScript NoteHead))
                            (format #t "\nPROBE ~a ~a y=~,6f x=~,6f\n" tag nm
                                    (- (ly:grob-relative-coordinate g sg Y)
                                       (ly:grob-relative-coordinate staff sg Y))
                                    (ly:grob-relative-coordinate g sg X)))))
                    all)))))
         (ly:prob-property page 'lines)))
      pages)))

\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GXA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16_"dolce" } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GXB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16^"dolce" } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GXC") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16_"dolce" e8. c2 \bar "|." } } } }
