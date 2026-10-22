\version "2.26.0"
%
% A SLUR OVER OR INSIDE A KNEED GRACE BEAM (session 944). A beamed stem ends on the line of its
% extreme beam rank toward its own side (lily/beam.cc:1113-1157 Beam::calc_stem_y); in a knee
% that line can stand past the primary, and the slur's encompass reads the stem's extent there
% (lily/slur-scoring.cc:111-160 get_encompass_info). Until session 944 Lily# read every grace
% stem at the primary line and a bound on a beamed grace column as a FLAGGED stem.
%
% SA / HA: the first grace turned down, a slur over the run (HE is HA without the knee).
% SB: three graces, the first turned down. SC: `d8 (down) e16` — e's beamlet BELOW the primary.
% IF / IG: a slur inside the grace body, bounds on beamed columns of a knee.
% IK: the down bound of an UP beam hangs from the face on its own stem's side, the primary's lower one.
%
% Output: PROBE <tag> p0=<first control point above the staff's middle line>
%         span=<last control point x - first>, in staff spaces.
%
% Run:  cmd /d /s /c "lilypond.exe -dno-print-pages grace-knee-slur.ly < NUL > out.log 2>&1"

\paper { indent = 0 ragged-right = ##t }
#(define (dump tag)
   (lambda (grob)
     (let* ((sys (ly:grob-system grob))
            (st (ly:grob-object grob 'staff-symbol))
            (cps (ly:grob-property grob 'control-points))
            (ref (ly:grob-relative-coordinate grob sys Y))
            (sy (if (ly:grob? st) (ly:grob-relative-coordinate st sys Y) 0)))
       (format #t "\nPROBE ~a p0=~a span=~a\n" tag
               (- (+ ref (cdr (car cps))) sy)
               (- (car (list-ref cps 3)) (car (car cps)))))))
D = \once \override Stem.direction = #DOWN
\score { \new Staff { \time 4/4 \fixed c' { \override Slur.after-line-breaking = #(dump "SA") c4( \grace { \D d16 e16 } f4) g4 a4 \bar "|." } } }
\score { \new Staff { \time 4/4 \fixed c' { \override Slur.after-line-breaking = #(dump "HA") c'4( \grace { \D d'16 e'16 } f'4) g'4 a'4 \bar "|." } } }
\score { \new Staff { \time 4/4 \fixed c' { \override Slur.after-line-breaking = #(dump "HE") c'4( \grace { d'16 e'16 } f'4) g'4 a'4 \bar "|." } } }
\score { \new Staff { \time 4/4 \fixed c' { \override Slur.after-line-breaking = #(dump "SB") c4( \grace { \D d16 e16 f16 } g4) a4 b4 \bar "|." } } }
\score { \new Staff { \time 4/4 \fixed c' { \override Slur.after-line-breaking = #(dump "SC") c4( \grace { \D d8 e16 } f4) g4 a4 \bar "|." } } }
\score { \new Staff { \time 4/4 \fixed c' { \override Slur.after-line-breaking = #(dump "IF") c4 \grace { d16( \D e16 \D f16) } g4 a4 b4 \bar "|." } } }
\score { \new Staff { \time 4/4 \fixed c' { \override Slur.after-line-breaking = #(dump "IG") c4 \grace { \D d16( e16 \D f16) } g4 a4 b4 \bar "|." } } }
\score { \new Staff { \time 4/4 \fixed c' { \override Slur.after-line-breaking = #(dump "IK") c4 \grace { \D d16( e16) } f4 g4 a4 \bar "|." } } }