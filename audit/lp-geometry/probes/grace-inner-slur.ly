\version "2.26.0"
%
% A SLUR WRITTEN IN GRACE TIME — anywhere, not only the appoggiatura's shape. LilyPond's
% Slur_engraver is a Voice engraver and knows nothing of grace time, so a slur mark on any
% grace note is paired as on any note; score-grace-settings pushes Slur.direction DOWN for
% the grace body, so a slur that STARTS there is DOWN. Until session 725 Lily# drew only the
% `\grace { g16( } a8)` shape and reported every other one dropped (LYS4020).
%
% INNER: both ends inside the body.  FIRST: from the first of two grace notes to the main
% note.  INTO: from a main note into the grace body (the start is ordinary time, so the
% direction is the ordinary rule's).
%
% Output: PROBE <tag> p0=<first control point above the staff's middle line>
%         span=<last control point x - first>, in staff spaces.

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
\score { { \override Slur.after-line-breaking = #(dump "INNER")
           c''4 \grace { d''16( e''16) } c''4 e''4 f''4 | } }
\score { { \override Slur.after-line-breaking = #(dump "FIRST")
           c''4 \grace { f'16( g'16 } a'8) c''8 d''4 e''4 | } }
\score { { \override Slur.after-line-breaking = #(dump "INTO")
           c''4( \grace { e''16) } d''4 e''4 f''4 | } }
\score { << { \override Slur.after-line-breaking = #(dump "VOICEONE")
              c''4 \grace { d''16( e''16) } c''4 e''4 f''4 } \\ { c'1 } >> }
\score { << { \override Slur.after-line-breaking = #(dump "VOICEACC")
              c''4 \acciaccatura { e''8 } d''4 e''4 f''4 } \\ { c'1 } >> }
\score { << { c'1 } \\ { \override Slur.after-line-breaking = #(dump "VOICETWO")
              c'4 \grace { d'16( e'16) } c'4 e'4 f'4 } >> }
