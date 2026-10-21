\version "2.26.0"
%% LP FIDELITY PROBE — the MUSICA FICTA (suggestion) accidental on a GRACE note (session 928).
%%
%% Run:  cmd /d /s /c "lilypond.exe -dbackend=null grace-editorial.ly < NUL > out.log 2>&1"
%%
%% LILYPOND-REF: lily/accidental-engraver.cc make_suggested_accidental — the suggestion's X
%%   parent and side support are the note head (and its stem), a grace's as any.
%% LILYPOND-REF: scm/music-functions.scm general-grace-settings names Accidental and
%%   AccidentalCautionary but NOT AccidentalSuggestion, so on a grace it keeps its own
%%   (font-size . -2) (scm/define-grobs.scm AccidentalSuggestion) — the PREDICTION, written
%%   before running: the grace suggestion's glyph is the full-size one's.
%%
%% Dumped per book: each AccidentalSuggestion's origin X and Y and its Y extent, and each
%% NoteHead's origin X and Y, all against the system.
#(define (dump-final tag)
   (lambda (layout pages)
     (for-each (lambda (page) (for-each (lambda (sys)
       (let ((sg (ly:prob-property sys 'system-grob)))
         (if (ly:grob? sg)
           (let* ((all (ly:grob-array->list (ly:grob-object sg 'all-elements))))
             (for-each (lambda (g)
               (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                 (if (memq nm '(NoteHead AccidentalSuggestion))
                     (format #t "\nPROBE ~a ~a x=~a y=~a ext=~a\n" tag nm
                             (ly:grob-relative-coordinate g sg X)
                             (ly:grob-relative-coordinate g sg Y)
                             (ly:grob-extent g sg Y)))))
               all)))))
       (ly:prob-property page 'lines))) pages)))
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "EGA") }
  \score { \new Staff \with { suggestAccidentals = ##t } { \time 4/4 \fixed c' { c4 \grace { fis!16 } e4 f4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "EGB") }
  \score { \new Staff \with { suggestAccidentals = ##t } { \time 4/4 \fixed c' { c4 fis!16 e8. f4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "EGC") }
  \score { \new Staff \with { suggestAccidentals = ##t } { \time 4/4 \fixed c' { c4 \grace { bes'!16 } e4 f4 g4 \bar "|." } } } }
%% Full-size CONTROLS of the suggestion's height (no grace): a natural alone, and a flat whose
%% box is not symmetric — both stand with the box bottom 0.46 over the staff's ink.
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "EGD") }
  \score { \new Staff \with { suggestAccidentals = ##t } { \time 4/4 \fixed c' { c4 e4 f!4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "EGE") }
  \score { \new Staff \with { suggestAccidentals = ##t } { \time 4/4 \fixed c' { c4 e4 bes4 g4 \bar "|." } } } }