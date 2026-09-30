\version "2.26.0"
%
% THE GRACE SLUR — the bow an \acciaccatura / \appoggiatura (or \grace { g16( } a8)) draws from
% the grace run's last note to its main note: an ordinary Slur (ly/grace-init.ly
% startGraceSlur / stopGraceSlur), DOWN by score-grace-settings. Until session 724 Lily# drew it
% from the grace group itself with fixed clearances (0.5 / 0.65 / 0.15) and the two heads as the
% only obstacles, ending short of a stem-down main note; since then it is laid out as any slur.
%
% C is the beam case: the main note is beamed on its left, so the slur (whose left bound, the
% grace column, stands inside that beam's span) must not hang from the beam
% (lily/slur-scoring.cc:549-554, !spanner_less || has_same_beam_) -- it ends on the head.
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
\score { { \override Slur.after-line-breaking = #(dump "ACC")
           c''4 \acciaccatura { e''8 } d''4 e''4 f''4 | } }
\score { { \override Slur.after-line-breaking = #(dump "APP")
           c''4 \appoggiatura { e''8 } d''4 e''4 f''4 | } }
\score { { \clef bass \override Slur.after-line-breaking = #(dump "BEAMED")
           d8 d d d d \grace { a16( } b8) a d | } }
