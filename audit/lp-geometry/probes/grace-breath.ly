\version "2.26.0"
%% LP FIDELITY PROBE — a breathing sign or caesura written on the LAST grace note (session 934;
%% Lily#'s grace { d16@breath } e4). The sign stands in a column of its own between the last
%% grace and the main note, which moves right by the sign and its space.
%%
%% Run:  cmd /d /s /c "lilypond.exe -dbackend=null grace-breath.ly < NUL > out.log 2>&1"
%%
%% LILYPOND-REF: scm/scheme-engravers.scm:43 Breathing_sign_engraver process-music — a Voice engraver, deaf to grace time;
%%   scm/define-grobs.scm:697-731 BreathingSign (break-aligned, first-note fixed-space 1.0).
%% GBB is the control without the sign. GBF-GBI (session 936) put the sign on an INNER grace:
%% its column stands between two grace columns, and the next grace is the sign's space x 0.8 on.
%% A breathing sign ends the automatic beam and the graces after it are not beamed again (GBH,
%% GBJ: Flags, no Beam after the sign); a caesura does not end it (GBG: one Beam).
%% GBK-GBL (session 938) put the sign before a grace REST: the rest's left edge stands where a
%% grace head would, the sign's space x 0.8 on.
%% GBM-GBO (session 939) put the sign in an \afterGrace: on its last grace the sign's column stands
%% before the EMPTY musical column at the grace's main moment, and the spring out of it is not
%% scaled (that column has no grace part).
%% Dumped: each NoteHead's, BreathingSign's, Flag's, Beam's and Rest's X extent, against the system.
#(define (dump-final tag)
   (lambda (layout pages)
     (for-each (lambda (page) (for-each (lambda (sys)
       (let ((sg (ly:prob-property sys 'system-grob)))
         (if (ly:grob? sg)
           (let* ((all (ly:grob-array->list (ly:grob-object sg 'all-elements))))
             (for-each (lambda (g)
               (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                 (if (memq nm '(NoteHead BreathingSign Flag Beam Rest))
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
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBF") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 \breathe e16 } f4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBG") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 \caesura e16 } f4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBH") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 \breathe e16 f16 } g4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBI") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 \breathe e16 \breathe } f4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBJ") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 e16 \breathe f16 g16 } a4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBK") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 \breathe r16 e16 } f4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBL") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 \breathe r16 } f4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBM") }
  \score { \new Staff { \time 4/4 \fixed c' { \afterGrace c2 { d16 \breathe } e2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBN") }
  \score { \new Staff { \time 4/4 \fixed c' { \afterGrace c2 { d16 \breathe e16 } f2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBO") }
  \score { \new Staff { \time 4/4 \fixed c' { \afterGrace c2 { d16 e16 \breathe } f2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBP") }
  \score { \new Staff { \time 4/4 \fixed c' { \afterGrace c2 { d16 } e2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBQ") }
  \score { \new Staff { \time 4/4 \fixed c' { \afterGrace c2 { d16 e16 f16 } g2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBR") }
  \score { \new Staff { \time 4/4 \fixed c' { \afterGrace c4 { d16 } e4 f2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBS") }
  \score { \new Staff { \time 4/4 \fixed c' { \afterGrace c1 { d16 } e1 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBT") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 \breathe s16 e16 } f4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBU") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 \breathe s16 } f4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBV") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 s16 e16 } f4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBW") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 s8 e16 } f4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBX") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 s16 } e4 c2 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "GBY") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { s16 d16 } e4 c2 \bar "|." } } } }