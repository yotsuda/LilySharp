\version "2.26.0"
%% LP FIDELITY PROBE — a breathing sign or caesura between two notes the auto-beamer would beam
%% (session 937). A breathing sign ends the automatic beam (its engraver is the Voice's, which the
%% Voice's Auto_beam_engraver hears); a caesura does not (its engraver is the Staff's).
%%
%% Run:  cmd /d /s /c "lilypond.exe -dbackend=null breath-beam.ly < NUL > out.log 2>&1"
%%
%% LILYPOND-REF: ly/engraver-init.ly:66 Staff — \consists Caesura_engraver; :384 Voice — \consists Breathing_sign_engraver.
%% Dumped: each NoteHead's, BreathingSign's, Flag's and Beam's X extent, against the system.
#(define (dump-final tag)
   (lambda (layout pages)
     (for-each (lambda (page) (for-each (lambda (sys)
       (let ((sg (ly:prob-property sys 'system-grob)))
         (if (ly:grob? sg)
           (let* ((all (ly:grob-array->list (ly:grob-object sg 'all-elements))))
             (for-each (lambda (g)
               (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                 (if (memq nm '(NoteHead BreathingSign Flag Beam))
                   (format #t "\nPROBE ~a ~a ext=~a\n" tag nm (ly:grob-extent g sg X)))))
               all)))))
       (ly:prob-property page 'lines))) pages)))
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "BBA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16 \breathe e16 f8 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "BBC") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16 \caesura e16 f8 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "BBD") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d8 \caesura e8 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "BBE") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d8 e8 c2 \bar "|." } } } }