\version "2.26.0"

%% A DYNAMIC ON A GRACE NOTE (session 916, HANDOFF §2 U8 ⒟).
%%
%% The ordinary Dynamic_engraver puts \f on a grace note at FULL size (general-grace-settings
%% names no DynamicText), centred on the grace head, sided off the grace column.
%%
%%   GDA  c'4 \grace { d'16\f } e'4 c'2     a dynamic under a grace
%%   GDB  c'4 d'16\f e'8. c'2               the full-size control
%%
%% Read where the page put it (page-post-process: DynamicText joins the outside-staff pass):
%% the label's refpoint, its baseline, above the staff's middle line.
%%
%% Output (LilyPond 2.26.0):  GDA -5.298316   GDB -5.518788
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
                      (if (eq? 'DynamicText (assq-ref (ly:grob-property g 'meta) 'name))
                          (format #t "\nPROBE ~a DYNAMIC y=~,6f fs=~a\n" tag
                                  (- (ly:grob-relative-coordinate g sg Y)
                                     (ly:grob-relative-coordinate staff sg Y))
                                  (ly:grob-property g 'font-size))))
                    all)))))
         (ly:prob-property page 'lines)))
      pages)))

\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GDA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\f } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GDB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16\f e8. c2 \bar "|." } } } }
