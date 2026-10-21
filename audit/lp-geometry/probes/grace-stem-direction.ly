\version "2.26.0"
%% LP FIDELITY PROBE — a grace note's stem turned by \once \override Stem.direction (session
%% 933; Lily#'s @stemUp / @stemDown on every stem of a grace body).
%%
%% Run:  cmd /d /s /c "lilypond.exe -dbackend=null grace-stem-direction.ly < NUL > out.log 2>&1"
%%
%% LILYPOND-REF: scm/music-functions.scm:652-656 score-grace-settings — every grace stem is
%%   forced UP, so a beam votes among forced stems (lily/beam.cc get_default_dir) and leaves
%%   each where it was: SDC / SDE draw KNEED beams (Lily# does not draw those yet, LYS4020).
%% SDU is the up control; SDV a lower voice's default DOWN grace; SUV that voice turned UP.
%% Dumped: each NoteHead's origin X, X extent and Y; each Stem / Flag / Beam's X and Y extent.
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
                   (format #t "\nPROBE ~a NoteHead x=~a ext=~a y=~a\n" tag
                           (ly:grob-relative-coordinate g sg X) (ly:grob-extent g sg X) (ly:grob-relative-coordinate g sg Y)))
                  ((memq nm '(Stem Flag Beam))
                   (format #t "\nPROBE ~a ~a x=~a ext=~a yext=~a dir=~a\n" tag nm (ly:grob-relative-coordinate g sg X)
                     (ly:grob-extent g sg X) (ly:grob-extent g sg Y) (ly:grob-property g 'direction))))))
               all)))))
       (ly:prob-property page 'lines))) pages)))
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "SDU") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 } e4 f4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "SDA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \once \override Stem.direction = #DOWN d16 } e4 f4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "SDA8") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \once \override Stem.direction = #DOWN d8 } e4 f4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "SDB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \once \override Stem.direction = #DOWN d16 \once \override Stem.direction = #DOWN e16 } f4 g4 a4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "SDC") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \once \override Stem.direction = #DOWN d16 e16 } f4 g4 a4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "SDE") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \once \override Stem.direction = #DOWN d16 e16 f16 } g4 a4 b4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "SDF") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \once \override Stem.direction = #DOWN d16 } \grace { e16 } f4 g4 a4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "SUV") }
  \score { \new Staff << \time 4/4 \fixed c' { c''4 d'' e'' f'' } \\ \fixed c' { c4 \grace { \once \override Stem.direction = #UP d16 } e4 f4 g4 } >> } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "SDV") }
  \score { \new Staff << \time 4/4 \fixed c' { c''4 d'' e'' f'' } \\ \fixed c' { c4 \grace { d16 } e4 f4 g4 } >> } }