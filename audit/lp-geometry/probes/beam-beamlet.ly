\version "2.26.0"
%
% WHICH SIDE DOES AN INNER STEM'S BEAMLET POINT?
%
% Lily# gives an interior beam member `left = min(count, prev)` and
% `right = min(count, next)` (BeamDetector.CreateBeamGroup). LilyPond does something else
% entirely: Beam_rhythmic_element starts with the stem's OWN count on BOTH sides
% (beaming-pattern.cc:50-62) and then, for interior stems only, picks ONE side to keep and
% chips the other (:116-183 the flag_directions block).
%
% The two agree whenever the stem's count exceeds only one neighbour's. They disagree when
% it exceeds BOTH: min() then deletes the beamlet on both sides and the stem draws no stub
% at all, while LilyPond keeps a full-height beamlet on the side the flag points to. That is
% the reported defect (8-16-8 draws no stub in Lily#).
%
% The rule, read off :121-183 — for 1 <= i < n-1, when the stem is not at a tuplet boundary
% and count(i) > min(count(i-1), count(i+1)):
%   point_right = (count(i+1) > count(i-1))                     if they differ
%               = (start_moment == cur_beat)                    if exactly one of
%                                                               {starts on a beat,
%                                                                ends on the next beat}
%               = rhythmic_importance(i) < rhythmic_importance(i+1)   otherwise
% then a correction pass (:161-167) turns a CENTER between a LEFT and a RIGHT into its
% neighbour's direction, and finally (:169-183) the OPPOSITE side is reduced by
% max(count(i) - count(neighbour on that side), 1).
%
% This probe measures the answer rather than trusting that reading. Every group is written
% with MANUAL brackets and starts on beat one, so the grouping is not also under test, and
% each bar is filled exactly so the beat arithmetic in the rule is the intended one.
%
% Output: PROBEB <name> STEM <i> beaming=((left ranks) . (right ranks))
%   The LENGTH of each list is that side's beam count; 0 beams on a side means no beamlet.

\paper { indent = 0 ragged-right = ##t }

#(define (dump-stem name)
   (let ((n -1))
     (lambda (grob)
       (set! n (1+ n))
       (format #t "PROBEB ~a STEM ~a beaming=~a\n" name n
               (ly:grob-property grob 'beaming)))))

sweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with { \override Stem.after-line-breaking = #(dump-stem name) }
      { \time 4/4 $music } #})

% counts 1 2 1 — the reported case: Lily# draws NO stub, LilyPond draws one.
pA = { c'8[ c'16 c'8] r8. r2 }
% counts 2 1 2 — the inner stem exceeds neither neighbour; both engines should agree.
pB = { c'16[ c'8 c'16] r4 r2 }
% counts 2 2 1 and 1 2 2 — the inner stem exceeds exactly ONE neighbour.
pC = { c'16[ c'16 c'8] r4 r2 }
pD = { c'8[ c'16 c'16] r4 r2 }
% four members, so the correction pass at :161-167 has something to correct.
pE = { c'16[ c'8 c'8 c'16] r8 r2 }
% counts 1 3 1 — the gap is two beams, so the chip of max(count-neighbour, 1) is > 1.
pF = { c'8[ c'32 c'8] r8 r16. r2 }

\score { \sweep "A" \pA }
\score { \sweep "B" \pB }
\score { \sweep "C" \pC }
\score { \sweep "D" \pD }
\score { \sweep "E" \pE }
\score { \sweep "F" \pF }
