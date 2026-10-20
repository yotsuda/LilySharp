\version "2.26.0"

%% A FINGERING ON A GRACE NOTE (session 917, HANDOFF §2 U8 ⒟).
%%
%% New_fingering_engraver makes an ordinary Fingering on a grace note at the grace's
%% font-size -8 (general-grace-settings), centred on the grace head, clearing it, the staff
%% and -- when the grace is beamed -- its stem by the Fingering's padding 0.5.
%%
%%   GFA  c'4 \grace { d'16-1 } e'4 c'2           a digit above a grace (the staff decides)
%%   GFB  c'4 \grace { b'16-3 } e'4 c'2           above a grace whose flagged stem reaches 2.7
%%   GFC  c'4 \grace { <d'-1 f'-3>16 } e'4 c'2    a grace chord: the lower digit goes DOWN
%%   GFD  c'4 \grace { a'16-1 b'16 } e'4 c'2      a BEAMED grace: its stem is a support
%%   GFE  c'4 <d'-1 f'-3>16 e'8. c'2              the full-size control of GFC
%%
%% Read where the page put it (page-post-process): the digit's baseline above the staff's
%% middle line, its X (the box's left edge), and the grace head's X.
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
                        (if (memq nm '(Fingering NoteHead Stem))
                            (format #t "\nPROBE ~a ~a y=~,6f x=~,6f ext=~a\n" tag nm
                                    (- (ly:grob-relative-coordinate g sg Y)
                                       (ly:grob-relative-coordinate staff sg Y))
                                    (ly:grob-relative-coordinate g sg X)
                                    (ly:grob-extent g g Y)))))
                    all)))))
         (ly:prob-property page 'lines)))
      pages)))

\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GFA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16-1 } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GFB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { b16-3 } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GFC") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { <d-1 f-3>16 } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GFD") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { a16-1 b16 } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GFE") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 <d-1 f-3>16 e8. c2 \bar "|." } } } }
