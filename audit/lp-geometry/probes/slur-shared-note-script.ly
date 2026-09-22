\version "2.26.0"
%% LP FIDELITY PROBE — which slur does a script read on the note where one slur ENDS and the
%% next STARTS, in the same measure? (HANDOFF §1.0 ⒳⁹, session 474.)
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe slur-shared-note-script.ly (four tiny books).
%%
%% Lily#'s ArticulationEngraver.CoveringSlurPiece picks the covering slur with the greatest START
%% MEASURE, so two slurs that start in the same measure tie and the first one added (the ENDED
%% slur) wins: the accent on the shared note reads 2.67 above the middle line, the same as the
%% book with only the ended slur, where the book with only the starting slur reads 2.8139.
%% Its remark says LilyPond prefers a RUNNING slur over an ended one:
%% LILYPOND-REF: lily/slur.cc:388-402 auxiliary_acknowledge_extra_object — slurs[0] before end_slurs[0].
%%
%% THE BOOKS are `lysc ly` twins of the Lily# books (never hand-written, RULES §5.5):
%%   SHR  c'4( d c@accent)( d)   — the accent's note ends slur 1 and starts slur 2
%%   END  c'4( d c@accent) d     — only the ended slur
%%   STA  c'4 d c@accent( d)     — only the starting slur
%%   PLN  c'4 d c@accent d       — no slur (the control)
%%
%% PREDICTION, written before running (with the sign): if LilyPond prefers the running slur,
%% SHR's accent equals STA's and stands ABOVE END's; if it reads the ended one, SHR equals END
%% (which is what Lily# does). Either way END should equal PLN if an ended slur does not touch
%% a script on its last note, as Lily# reads it (2.67 both).
%%
%% NOTE: inside #(...) the comment character is `;`, not `%%`.

#(define (probe-dump-pages layout pages)
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let ((sg (ly:prob-property (car ls) 'system-grob)))
                   (if (ly:grob? sg)
                       (let ((all (ly:grob-object sg 'all-elements)))
                         (if (ly:grob-array? all)
                             (for-each
                              (lambda (g)
                                (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                  ;; The script's refpoint and ink about the SYSTEM, and the
                                  ;; staff symbol's refpoint (its middle line) about the same.
                                  (if (memq nm '(Script StaffSymbol Slur))
                                      (format #t "PROBEV GROB ~a ~a name=~a rel=~a ext=(~a . ~a) x=~a\n"
                                              n i nm
                                              (ly:grob-relative-coordinate g sg Y)
                                              (car (ly:grob-extent g sg Y))
                                              (cdr (ly:grob-extent g sg Y))
                                              (ly:grob-relative-coordinate g sg X)))))
                              (ly:grob-array->list all))))))
                 (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEV BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

\book {
  \probeTag "SHR"
  \score { \new Staff { \relative c' { c'4 ( d c-\accent ) ( d ) | } }
           \layout { indent = 0\mm } }
}

\book {
  \probeTag "END"
  \score { \new Staff { \relative c' { c'4 ( d c-\accent ) d | } }
           \layout { indent = 0\mm } }
}

\book {
  \probeTag "STA"
  \score { \new Staff { \relative c' { c'4 d c-\accent ( d ) | } }
           \layout { indent = 0\mm } }
}

\book {
  \probeTag "PLN"
  \score { \new Staff { \relative c' { c'4 d c-\accent d | } }
           \layout { indent = 0\mm } }
}
