\version "2.26.0"
%% LP FIDELITY PROBE — the MUSICA FICTA (suggestion) accidental on a CHORD MEMBER (session 929).
%%
%% Run:  cmd /d /s /c "lilypond.exe -dbackend=null editorial-chord.ly < NUL > out.log 2>&1"
%%
%% LILYPOND-REF: lily/accidental-engraver.cc make_suggested_accidental — one
%%   AccidentalSuggestion per note head that needs one, its X parent that head, so a chord
%%   member's stands over the member; two of them are both outside-staff movers at priority 0
%%   and stack (lily/axis-group-interface.cc avoid_outside_staff_collisions).
%%
%% Dumped per book: each AccidentalSuggestion's glyph, origin X and Y; each NoteHead's
%% origin X and Y; the StaffSymbol's Y — all against the system.
#(define (dump-final tag)
   (lambda (layout pages)
     (for-each (lambda (page) (for-each (lambda (sys)
       (let ((sg (ly:prob-property sys 'system-grob)))
         (if (ly:grob? sg)
           (let* ((all (ly:grob-array->list (ly:grob-object sg 'all-elements))))
             (for-each (lambda (g)
               (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                 (if (memq nm '(NoteHead AccidentalSuggestion StaffSymbol))
                     (format #t "\nPROBE ~a ~a ~a x=~a y=~a\n" tag nm
                             (if (eq? nm 'AccidentalSuggestion) (ly:grob-property g 'glyph-name) "")
                             (ly:grob-relative-coordinate g sg X)
                             (ly:grob-relative-coordinate g sg Y)))))
               all)))))
       (ly:prob-property page 'lines))) pages)))
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "ECA") }
  \score { \new Staff \with { suggestAccidentals = ##t } { \time 4/4 \fixed c' { c4 <d f!>4 e4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "ECB") }
  \score { \new Staff \with { suggestAccidentals = ##t } { \time 4/4 \fixed c' { c4 <dis f!>4 e4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "ECC") }
  \score { \new Staff \with { suggestAccidentals = ##t } { \time 4/4 \fixed c' { c4 \grace { <d f!>16 } e4 g4 a4 \bar "|." } } } }