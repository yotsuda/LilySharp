\version "2.26.0"

%% A GLISSANDO FROM OR TO A GRACE NOTE (session 920, HANDOFF §2 U8 ⒟).
%%
%% Glissando_engraver bounds its line at note HEADS, a grace's as any: from the start head's
%% right edge to the target head's left edge (or its accidental's), each end pulled the
%% padding 0.5 inward ALONG the line; a line too short for both paddings is not drawn.
%%
%%   GGA  c'4 \grace { d'16\glissando } a'4 c'2        from a grace to the main note
%%   GGB  c'4 d'16\glissando a'8. c'2                  the full-size control
%%   GGC  c'4\glissando \grace { g'16 } a'4 c'2        from a main note INTO the grace (it ends there)
%%   GGD  c'4 \grace { d'16\glissando e'16 } a'4 c'2   grace to grace: too short, not drawn
%%   GGS  r2 d'4\glissando \grace { d'8 } c'4         into a grace at the SAME pitch: a short level line
%%
%% Read where the page put it (page-post-process): the Glissando's stencil (its draw-line
%% ends, not the X-extent, which the round cap widens by half the 0.1 line) and the NoteHeads.
%%
%% Output (LilyPond 2.26.0), X relative to the system, Y above the staff's middle line:
%%   GGA heads 8.585 / 10.986796 / 12.925423, line (12.132019, -2.054644) -> (12.698138, -0.945356)
%%   GGB heads 8.585 / 13.4892 / 16.2434,     line (15.086884, -2.095195) -> (15.949916, -0.904805)
%%   GGC heads 8.585 / 11.186796 / 13.125423, line (10.161340, -2.580548) -> (10.914656, -1.419452)
%%   GGD no line (empty extent).
%%   GGS heads 12.983045 / 15.384841 (grace d) / 17.145194, line (14.787245, -2.5) -> (14.884841, -2.5): horizontal, 0.097596 long.
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
                        (if (memq nm '(Glissando NoteHead))
                            (format #t "\nPROBE ~a ~a y=~,6f x=~,6f xext=~a yext=~a expr=~a\n" tag nm
                                    (- (ly:grob-relative-coordinate g sg Y)
                                       (ly:grob-relative-coordinate staff sg Y))
                                    (ly:grob-relative-coordinate g sg X)
                                    (ly:grob-extent g sg X) (ly:grob-extent g sg Y) (let ((s (ly:grob-property g (quote stencil)))) (if (and (eq? nm (quote Glissando)) (ly:stencil? s)) (ly:stencil-expr s) ""))))))
                    all)))))
         (ly:prob-property page 'lines)))
      pages)))

\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GGA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\glissando } a4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GGB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16\glissando a8. c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GGC") }
  \score { \new Staff { \time 4/4 \fixed c' { c4\glissando \grace { g16 } a4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GGD") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\glissando e16 } a4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GGS") }
  \score { \new Staff { \time 4/4 \fixed c' { r2 d4\glissando \grace { d8 } c4 \bar "|." } } } }

