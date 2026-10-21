\version "2.26.0"
%% LP FIDELITY PROBE — a KNEED grace beam (session 935; Lily#'s @stemDown / @stemUp on some of a
%% beamed grace run's stems). Every grace stem is a forced one (score-grace-settings), so the
%% beam votes (lily/beam.cc get_default_dir) and leaves each stem where it was.
%%
%% Run:  cmd /d /s /c "lilypond.exe -dbackend=null grace-knee.ly < NUL > out.log 2>&1"
%%
%% KA: d turned down, e up — the beam UP (the tie broken by e's heads), the stack ABOVE the
%%   first line (beam-segments vertical-count +1: toward the first stem's head).
%% KG: d up, e turned down — the knee correction pushes e 0.787939 right.
%% KF: d up, e f turned down — the beam DOWN, the stack BELOW (vertical-count -1).
%% K16 is a FULL-SIZE kneed sixteenth pair, the same quanter term (beam.quant.knee.sixteenth.*). KE is the unturned control; KB / KC / KD are further knees (not on the ledger).
%% Dumped: each NoteHead's X extent and Y, each Stem's X / Y extent and direction, the Beam's.
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
                   (format #t "\nPROBE ~a NoteHead x=~a y=~a\n" tag
                           (ly:grob-extent g sg X) (ly:grob-relative-coordinate g sg Y)))
                  ((eq? nm 'Stem)
                   (format #t "\nPROBE ~a Stem x=~a yext=~a dir=~a\n" tag (ly:grob-extent g sg X)
                     (ly:grob-extent g sg Y) (ly:grob-property g 'direction)))
                  ((eq? nm 'Beam)
                   (format #t "\nPROBE ~a Beam x=~a yext=~a dir=~a pos=~a segs=~a\n" tag (ly:grob-extent g sg X)
                     (ly:grob-extent g sg Y) (ly:grob-property g 'direction) (ly:grob-property g 'positions)
                     (ly:grob-property g 'beam-segments))))))
               all)))))
       (ly:prob-property page 'lines))) pages)))
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "KA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \once \override Stem.direction = #DOWN d16 e16 } f4 g4 a4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "KB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \once \override Stem.direction = #DOWN d16 e16 f16 } g4 a4 b4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "KC") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d8 \once \override Stem.direction = #DOWN e8 } f4 g4 a4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "KD") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \once \override Stem.direction = #DOWN d16 \once \override Stem.direction = #DOWN e16 f16 } g4 a4 b4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "KE") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 e16 } f4 g4 a4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "KF") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 \once \override Stem.direction = #DOWN e16 \once \override Stem.direction = #DOWN f16 } g4 a4 b4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "KG") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16 \once \override Stem.direction = #DOWN e16 } f4 g4 a4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "K16") }
  \score { \new Staff { \time 2/4 \fixed c' { \once \override Stem.direction = #DOWN d16[ e16] g8 a4 \bar "|." } } } }