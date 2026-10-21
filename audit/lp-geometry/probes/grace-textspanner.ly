\version "2.26.0"

%% A TEXT SPANNER ("rit.") STARTING OR ENDING ON A GRACE NOTE (session 921, HANDOFF §2 U8 ⒟).
%%
%% Text_spanner_engraver bounds its spanner at note columns, a grace's as any: the label
%% starts the bound padding 0.25 right of the grace head, and the line keeps its level.
%%
%%   GRA  c'4 \grace { d'16\startTextSpan } e'4 f'4 g'4\stopTextSpan   from a grace
%%   GRB  c'4 d'16\startTextSpan e'8. f'4 g'4\stopTextSpan             the full-size control
%%   GRC  c'4\startTextSpan e'4 \grace { f'16\stopTextSpan } g'4 a'4   ending on a grace
%%
%% Read where the page put it (page-post-process): the TextSpanner's X-extent and Y, the
%% NoteHeads' X.
%%
%% Output (LilyPond 2.26.0), X relative to the system:
%%   GRA grace head 10.986796, spanner (11.236796 . 19.382013), y 2.85.
%%   GRB d head 13.4892,       spanner (13.7392 . 25.755855),    y 2.85.
%%   GRC c head 8.585, grace head 14.239041, spanner (8.835 . 14.758355): the right end is the
%%   bound column's CENTRE-ish, which Lily# does not port for any column (TextSpannerEngraver's
%%   remark) -- no ledger point reads it.
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
                        (if (memq nm '(TextSpanner NoteHead))
                            (format #t "\nPROBE ~a ~a y=~,6f x=~,6f xext=~a yext=~a\n" tag nm
                                    (- (ly:grob-relative-coordinate g sg Y)
                                       (ly:grob-relative-coordinate staff sg Y))
                                    (ly:grob-relative-coordinate g sg X)
                                    (ly:grob-extent g sg X) (ly:grob-extent g sg Y)))))
                    all)))))
         (ly:prob-property page 'lines)))
      pages)))

rit = { \override TextSpanner.bound-details.left.text = \markup \italic "rit." }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GRA") }
  \score { \new Staff { \time 4/4 \rit \fixed c' { c4 \grace { d16\startTextSpan } e4 f4 g4\stopTextSpan \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GRB") }
  \score { \new Staff { \time 4/4 \rit \fixed c' { c4 d16\startTextSpan e8. f4 g4\stopTextSpan \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GRC") }
  \score { \new Staff { \time 4/4 \rit \fixed c' { c4\startTextSpan e4 \grace { f16\stopTextSpan } g4 a4 \bar "|." } } } }
