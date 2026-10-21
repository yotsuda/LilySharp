\version "2.26.0"
%% LP FIDELITY PROBE — a grace note's HEAD drawn another way (session 931): the dead note's
%% cross (\deadNote, Lily#'s @dead), and the parentheses round it (\parenthesize).
%%
%% Run:  cmd /d /s /c "lilypond.exe -dbackend=null grace-head-style.ly < NUL > out.log 2>&1"
%%
%% LILYPOND-REF: ly/property-init.ly deadNote — a tweak of NoteHead.style to cross (and
%%   glyph-name), on a grace's NoteHead as any, so the cross is the grace font's (-3).
%% LILYPOND-REF: lily/parenthesis-engraver.cc — a Parentheses grob round the grobs a
%%   parenthesized event makes; general-grace-settings names no Parentheses.
%%
%% Dumped per book: each NoteHead's origin X, its X and Y extents and glyph; each Stem's X; each Parentheses'
%% X and Y extents — against the system.
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
                   (format #t "\nPROBE ~a NoteHead ~a x=~a ext=~a yext=~a\n" tag
                           (ly:grob-property g 'glyph-name)
                           (ly:grob-relative-coordinate g sg X) (ly:grob-extent g sg X) (ly:grob-extent g sg Y)))
                  ((eq? nm 'Parentheses)
                   (format #t "\nPROBE ~a Parentheses ext=~a yext=~a fs=~a\n" tag
                           (ly:grob-extent g sg X) (ly:grob-extent g sg Y) (ly:grob-property g 'font-size)))
                  ((eq? nm 'Stem)
                   (format #t "\nPROBE ~a Stem x=~a\n" tag (ly:grob-relative-coordinate g sg X)))
                  ((eq? nm 'StaffSymbol)
                   (format #t "\nPROBE ~a StaffSymbol y=~a\n" tag (ly:grob-relative-coordinate g sg Y))))))
               all)))))
       (ly:prob-property page 'lines))) pages)))
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "DHA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \deadNote d16 } e4 f4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "DHB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \deadNote d16 e8. f4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "PRA") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \grace { \parenthesize d16 } e4 f4 g4 \bar "|." } } } }
\book { \paper { indent = 0 ragged-right = ##t page-post-process = #(dump-final "PRB") }
  \score { \new Staff { \time 4/4 \fixed c' { c4 \parenthesize d16 e8. f4 g4 \bar "|." } } } }