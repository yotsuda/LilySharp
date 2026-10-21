\version "2.26.0"
%% LP FIDELITY PROBE — a HALF-TIE (\laissezVibrer, \repeatTie) on a GRACE note (session 930).
%%
%% Run:  cmd /d /s /c "lilypond.exe -dbackend=null grace-semitie.ly < NUL > out.log 2>&1"
%%
%% LILYPOND-REF: lily/laissez-vibrer-engraver.cc acknowledge_note_head — one tie per head,
%%   a grace's as any; lily/repeat-tie-engraver.cc inherits it. general-grace-settings
%%   (scm/music-functions.scm) names neither tie, so the bow keeps its own details; what is
%%   the grace's is the note column it is formatted against (the -3 head, the grace stem).
%%
%% Dumped per book: each half-tie's control points against the system (the grob's X and Y
%% added), each NoteHead's origin X and its X extent, and the StaffSymbol's Y.
#(define (dump-final tag)
   (lambda (layout pages)
     (for-each (lambda (page) (for-each (lambda (sys)
       (let ((sg (ly:prob-property sys 'system-grob)))
         (if (ly:grob? sg)
           (let* ((all (ly:grob-array->list (ly:grob-object sg 'all-elements))))
             (for-each (lambda (g)
               (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                 (cond
                  ((memq nm '(LaissezVibrerTie RepeatTie))
                   (let ((gx (ly:grob-relative-coordinate g sg X))
                         (gy (ly:grob-relative-coordinate g sg Y)))
                     (format #t "\nPROBE ~a ~a cp=~a\n" tag nm
                             (map (lambda (p) (cons (+ gx (car p)) (+ gy (cdr p))))
                                  (ly:grob-property g 'control-points)))))
                  ((eq? nm 'NoteHead)
                   (format #t "\nPROBE ~a NoteHead x=~a ext=~a\n" tag
                           (ly:grob-relative-coordinate g sg X) (ly:grob-extent g sg X)))
                  ((eq? nm 'StaffSymbol)
                   (format #t "\nPROBE ~a StaffSymbol y=~a\n" tag (ly:grob-relative-coordinate g sg Y))))))
               all)))))
       (ly:prob-property page 'lines))) pages)))
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "LVA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\laissezVibrer } e4 f4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "LVB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16\laissezVibrer e8. f4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "RTA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { d16\repeatTie } e4 f4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "RTB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 d16\repeatTie e8. f4 g4 \bar "|." } } } }