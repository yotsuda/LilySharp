\version "2.26.0"
%% LP FIDELITY PROBE — a breathing sign or caesura written on the LAST grace note (session 934;
%% Lily#'s grace { d16@breath } e4). The sign stands in a column of its own between the last
%% grace and the main note, which moves right by the sign and its space.
%%
%% Run:  cmd /d /s /c "lilypond.exe -dbackend=null grace-breath.ly < NUL > out.log 2>&1"
%%
%% LILYPOND-REF: scm/scheme-engravers.scm:43 Breathing_sign_engraver process-music — a Voice engraver, deaf to grace time;
%%   scm/define-grobs.scm:697-731 BreathingSign (break-aligned, first-note fixed-space 1.0).
%% GBB is the control without the sign; an INNER grace's sign (Lab sessions/p923/br BRD) is
%% not ported (Lily# reports it).
%% Dumped: each NoteHead's and BreathingSign's X extent, against the system.
#(define (dump-final tag)
   (lambda (layout pages)
     (for-each (lambda (page) (for-each (lambda (sys)
       (let ((sg (ly:prob-property sys 'system-grob)))
         (if (ly:grob? sg)
           (let* ((all (ly:grob-array->list (ly:grob-object sg 'all-elements))))
             (for-each (lambda (g)
               (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                 (if (memq nm '(NoteHead BreathingSign))
                   (format #t "\nPROBE ~a ~a ext=~a\n" tag nm (ly:grob-extent g sg X)))))
               all)))))
       (ly:prob-property page 'lines))) pages)))
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 \breathe } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBC") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 \caesura } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBD") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 e16 \breathe } f4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBE") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { <d f>16 \breathe } e4 c2 \bar "|." } } } }