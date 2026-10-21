\version "2.26.0"
%% LP FIDELITY PROBE — a grace note's head in the other STYLES (session 932): diamond,
%% triangle, slash, xcircle (Lily#'s @notehead(…)). The cross is grace-head-style.ly's.
%%
%% Run:  cmd /d /s /c "lilypond.exe -dbackend=null grace-head-shapes.ly < NUL > out.log 2>&1"
%%
%% LILYPOND-REF: scm/output-lib.scm note-head::calc-glyph-name — the style picks the glyph,
%%   read at the grace NoteHead's font-size -3 (general-grace-settings); the spacing reads the
%%   head's own extent, so a wider glyph moves the columns.
%%
%% Per style two books: one grace before the main note, and two graces (the first styled).
%% Dumped: each NoteHead's glyph, origin X and X extent; each Stem's X — against the system.
#(define (dump-final tag)
   (lambda (layout pages)
     (for-each (lambda (page) (for-each (lambda (sys)
       (let ((sg (ly:prob-property sys 'system-grob)))
         (if (ly:grob? sg)
           (let* ((all (ly:grob-array->list (ly:grob-object sg 'all-elements))))
             (for-each (lambda (g)
               (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                 (cond
                  ((eq? nm 'NoteHead)
                   (format #t "\nPROBE ~a NoteHead ~a x=~a ext=~a\n" tag
                           (ly:grob-property g 'glyph-name)
                           (ly:grob-relative-coordinate g sg X) (ly:grob-extent g sg X)))
                  ((eq? nm 'Stem)
                   (format #t "\nPROBE ~a Stem x=~a\n" tag (ly:grob-relative-coordinate g sg X))))))
               all)))))
       (ly:prob-property page 'lines))) pages)))
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "HSD") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \once \override NoteHead.style = #'diamond d16 } e4 f4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "HSD2") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \once \override NoteHead.style = #'diamond d16 e16 } f4 g4 a4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "HST") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \once \override NoteHead.style = #'triangle d16 } e4 f4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "HST2") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \once \override NoteHead.style = #'triangle d16 e16 } f4 g4 a4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "HSS") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \once \override NoteHead.style = #'slash d16 } e4 f4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "HSS2") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \once \override NoteHead.style = #'slash d16 e16 } f4 g4 a4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "HSX") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \once \override NoteHead.style = #'xcircle d16 } e4 f4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "HSX2") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \once \override NoteHead.style = #'xcircle d16 e16 } f4 g4 a4 \bar "|." } } } }