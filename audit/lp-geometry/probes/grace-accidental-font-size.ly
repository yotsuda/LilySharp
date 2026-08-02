\version "2.26.0"
%
% WHICH DESIGN DOES A GRACE'S ACCIDENTAL PRINT AT?
%
% grace-column-width.ly GCWA dumps the grace sharp's extent in its column as
% (-1.0429565774421805 . -0.3500000000000003), i.e. 0.692957 wide. The 20 design's sharp
% is 1.100000 and magstep(-3) = 0.707107, which would give 0.777817; 0.692957 / 1.100000 =
% 0.629961 = magstep(-4). So either the grace's accidental is not at the grace's font-size,
% or its glyph is not the 1.1-wide sharp. Ask the grob rather than argue.
%
% THE ANSWER (2026-08-02): font-size = -4, against the head's -3. The recipe is per-grob and
% it is in the SOURCE, not in ly/grace-init.ly where every comment in Lily# used to point:
%   scm/music-functions.scm:635-648 general-grace-settings
%     (Voice NoteHead font-size -3) ... (Voice Accidental font-size -4)
%     (Voice AccidentalCautionary -4) (Voice Script -3) (Voice Fingering -8)
%     (Voice StringNumber -8) (Voice TabNoteHead -4)
% so a grace's head reads the FOURTEEN design and its accidental the THIRTEEN. Kept because
% the ledger entry grace.column.accidental.step had asserted the FOURTEEN, and a probe that
% falsified a written cause is worth more than one that confirms a number.
%
% Expected output:
%   PROBEAS GCWA HEAD font-size=-3    xext=(-0.0 . 0.9179386191980385)
%   PROBEAS GCWA ACC  font-size=-4    xext=(-0.0 . 0.6929565774421802)  glyph=accidentals.sharp
%   PROBEAS MAIN ACC  font-size=unset xext=(-0.0 . 1.0999999999999999)
\paper { indent = 0 ragged-right = ##t }

#(define (dump-acc name)
   (lambda (grob)
     (let* ((st (ly:grob-property grob 'stencil))
            (fs (ly:grob-property grob 'font-size))
            (sz (ly:grob-property grob 'font-size 'unset)))
       (format #t "PROBEAS ~a ACC font-size=~a xext=~a yext=~a glyph=~a\n" name
               sz
               (if (ly:stencil? st) (ly:stencil-extent st X) 'none)
               (if (ly:stencil? st) (ly:stencil-extent st Y) 'none)
               (ly:grob-property grob 'glyph-name)))))

#(define (dump-head name)
   (lambda (grob)
     (let* ((st (ly:grob-property grob 'stencil)))
       (format #t "PROBEAS ~a HEAD font-size=~a xext=~a\n" name
               (ly:grob-property grob 'font-size 'unset)
               (if (ly:stencil? st) (ly:stencil-extent st X) 'none)))))

sweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with { \override Accidental.after-line-breaking = #(dump-acc name)
                         \override NoteHead.after-line-breaking = #(dump-head name) }
      { $music } #})

% The GCWA regime itself.
\score { \sweep "GCWA" { \time 4/4 \grace { d'16 eis' } f'4 g'2 r4 } }

% Control: the SAME sharp on an ordinary note, same book shape.
\score { \sweep "MAIN" { \time 4/4 eis'4 f'4 g'2 r4 } }
