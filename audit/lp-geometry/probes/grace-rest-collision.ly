\version "2.26.0"
%
% A REST IN A GRACE BODY, AFTER A MAIN NOTE THAT STILL SOUNDS. The grace sits at (X, -g), before
% the previous note's end (X, 0), so Rest_collision_engraver counts that note busy
% (lily/rest-collision-engraver.cc:55-80, "Include notes that started any time") and the rest
% takes the rests-and-notes branch of lily/rest-collision.cc calc_positioning_done. The rest has
% no direction, so the branch reads its column's, i.e. its stem's -- and a grace stem is UP
% (scm/music-functions.scm score-grace-settings). An ordinary rest after the same note has no
% direction to move by and stays on the middle line (the control).
%
% Until session 723 Lily# left the grace rest on the middle line, and a slur over the run ran
% through it where LilyPond lifts both.
%
% Output: PROBE <tag> ref = the Rest's reference point (its glyph origin) above the staff's
% middle line, in staff spaces; PROBE SLUR-n p0 = the n-th slur's first control point above it.

\paper { indent = 0 ragged-right = ##t }
#(define (dump tag)
   (lambda (grob)
     (let* ((sys (ly:grob-system grob))
            (st (ly:grob-object grob 'staff-symbol))
            (ref (ly:grob-relative-coordinate grob sys Y))
            (sy (if (ly:grob? st) (ly:grob-relative-coordinate st sys Y) 0)))
       (format #t "\nPROBE ~a ref=~a\n" tag (- ref sy)))))
{
  e''4 \grace { \tweak after-line-breaking #(dump "GRACE-HIGH") r16 f''16 } g''4
  e'4 \grace { \tweak after-line-breaking #(dump "GRACE-LOW") r16 f'16 } g'4 |
  c'4 \grace { \tweak after-line-breaking #(dump "GRACE-CLEAR") r8 } c'4
  e''4 \tweak after-line-breaking #(dump "CONTROL") r16 f''8. |
}

% The slurs over the same runs: LilyPond's bow clears the lifted rest (slur-scoring.cc:117-122,
% a stemless column encompassed by its Y extent).
#(define slur-count 0)
#(define (dump-slur grob)
   (let* ((sys (ly:grob-system grob))
          (st (ly:grob-object grob 'staff-symbol))
          (cps (ly:grob-property grob 'control-points))
          (ref (ly:grob-relative-coordinate grob sys Y))
          (sy (if (ly:grob? st) (ly:grob-relative-coordinate st sys Y) 0)))
     (set! slur-count (1+ slur-count))
     (format #t "\nPROBE SLUR-~a p0=~a\n" slur-count (- (+ ref (cdr (car cps))) sy))))
\score {
  { \override Slur.after-line-breaking = #dump-slur
    e''4( \grace { r16 f''16 } g''4) e''4( \grace { r8 } g''4) | }
}

% A slur over a FLAGGED grace (session 723, second find): the grace stem's tip is general-grace-
% settings' length-fraction 0.8 (b'16: 2.7 above its head), which Lily#'s slur obstacle read as
% 3.5 x the head's magstep(-3) = 2.475 until then -- the bow sat 0.5 low.
\score {
  { \override Slur.after-line-breaking = #dump-slur
    a'4( \grace { b'16 } c''4) c'4( \grace { a16 } b4) | }
}
