\version "2.26.0"
%
% A GRACE BEAM'S BEAMLETS (session 944). A grace beam is beamified as an ordinary one, at grace
% moments (lily/template-engraver-for-beams.cc:40-78), so a stem whose value carries more beams
% than its neighbour keeps them on that side as a BEAMLET, and its lines take the ranks
% lily/beam.cc:294-373 Beam::calc_beaming assigns. Until session 944 Lily# drew only the lines
% two neighbours share, with no beamlet, and stacked every secondary on the first stem's head side.
%
% B1 `d16 e8 f16`: a beamlet right of d and left of f. B2 `d8 e16 f8`: e's on its LEFT
% (rhythmic importance). B5 `d8. e16`. B9 `d16 (down) e8 f16`: a knee, d's beamlet ABOVE, f's BELOW.
% SC `d8 (down) e16`: e's beamlet BELOW the up beam, so e's up stem ends on the primary.
%
% Output: PROBE <tag> stem=<i> beaming=<Stem.beaming: rank lists left . right> top=<the stem's
%         upper end above its (single) head> bottom=<its lower end below it>.
%
% Run:  cmd /d /s /c "lilypond.exe -dno-print-pages grace-beamlet.ly < NUL > out.log 2>&1"

\paper { indent = 0 ragged-right = ##t }
#(define (dump tag)
   (lambda (beam)
     (let ((stems (ly:grob-array->list (ly:grob-object beam 'stems)))
           (sys (ly:grob-system beam)))
       (let loop ((ss stems) (i 0))
         (if (pair? ss)
             (let* ((s (car ss))
                    (head (car (ly:grob-array->list (ly:grob-object s 'note-heads))))
                    (hy (ly:grob-relative-coordinate head sys Y))
                    (ext (ly:grob-extent s sys Y)))
               (format #t "\nPROBE ~a stem=~a beaming=~a top=~a bottom=~a\n" tag i
                       (ly:grob-property s 'beaming) (- (cdr ext) hy) (- hy (car ext)))
               (loop (cdr ss) (1+ i))))))))
D = \once \override Stem.direction = #DOWN
\score { \new Staff { \time 4/4 \fixed c' { \override Beam.after-line-breaking = #(dump "B1") c4 \grace { d16 e8 f16 } g4 a4 b4 \bar "|." } } }
\score { \new Staff { \time 4/4 \fixed c' { \override Beam.after-line-breaking = #(dump "B2") c4 \grace { d8 e16 f8 } g4 a4 b4 \bar "|." } } }
\score { \new Staff { \time 4/4 \fixed c' { \override Beam.after-line-breaking = #(dump "B5") c4 \grace { d8. e16 } g4 a4 b4 \bar "|." } } }
\score { \new Staff { \time 4/4 \fixed c' { \override Beam.after-line-breaking = #(dump "B9") c4 \grace { \D d16 e8 f16 } g4 a4 b4 \bar "|." } } }
\score { \new Staff { \time 4/4 \fixed c' { \override Beam.after-line-breaking = #(dump "SC") c4 \grace { \D d8 e16 } f4 g4 a4 \bar "|." } } }