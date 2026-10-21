\version "2.26.0"

%% A HAIRPIN'S OWN OUTLINE against a head under it (session 918).
%%
%% The wedge is side-positioned off the note columns by its stencil's skyline. LilyPond's
%% skyline builder takes each drawn arm as a segment LENGTHENED by half the 0.1 rule at both
%% ends along X (keeping its end heights) and shifted by the same half outward
%% (lily/include/lazy-skyline-pair.hh add_segment). Lily# shifted it without lengthening until
%% session 918: a crescendo bound near its tip sat 0.003 shallower, a decrescendo 0.0045
%% deeper. Found under the grace-hairpin books (grace-hairpin-start.ly), whose full-size
%% control showed the same residual.
%%
%%   GHH  c'4 d'4\< e'4 f'4\!     a crescendo, the d' head binds near the tip
%%   GHI  c'4 d'4\> e'4 f'4\!     a decrescendo, the d' head binds at the open end
%%   GHJ  g'4 a'4\< b'4 c''4\!    the DOWN stems of b' and c'' bind (thin supports under the arm)
%%
%% Output (LilyPond 2.26.0): Hairpin y = -3.816845 / -4.357101 / -4.270591
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
                        (if (memq nm '(Hairpin NoteHead DynamicText))
                            (format #t "\nPROBE ~a ~a y=~,6f x=~,6f xext=~a\n" tag nm
                                    (- (ly:grob-relative-coordinate g sg Y)
                                       (ly:grob-relative-coordinate staff sg Y))
                                    (ly:grob-relative-coordinate g sg X)
                                    (ly:grob-extent g sg X)))))
                    all)))))
         (ly:prob-property page 'lines)))
      pages)))

\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GHH") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d4\< e4 f4\! \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GHI") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d4\> e4 f4\! \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GHJ") }
  \score { \new Staff { \time 4/4 \fixed c' { g4 a4\< b4 c'4\! \bar "|." } } } }
