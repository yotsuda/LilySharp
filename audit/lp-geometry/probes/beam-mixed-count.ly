\version "2.26.0"
%
% WHAT HAPPENS TO A BEAM WHOSE LINE COUNT CHANGES ALONG THE GROUP?
%
% Every beam point in the ledger so far reads a group whose members all carry the SAME number
% of beam lines. LilyPond decides a beamed stem's ideal length from a beam count that is NOT
% the stem's own: lily/stem.cc:1158 asks Beam::get_direction_beam_count (lily/beam.cc:1517-1532),
% which is the MAXIMUM multiplicity over every stem pointing that way. LilyPond's own source
% says why, at stem.cc:1196-1202:
%
%     UGH
%     It seems that also for ideal minimum length, we must use
%     the maximum beam count (for this direction):
%       \score { \relative c'' { a8[ a32] } }
%     must be horizontal.
%
% If that is what LilyPond does, a mixed group's stems all take their ideal_y from ONE count,
% the two ends' ideals come out equal, and least_squares_positions (lily/beam-quanting.cc:551-580)
% takes the branch that does not fit a line at all: the beam is flat and least-squares-dy is 0,
% which is what wakes the two scorers that keep it flat (HANDOFF section 1, 2026-07-31).
%
% Output: PROBEMC <name> BEAM stems=<n> pos=<positions> dirs=<...> info=<per-stem stem-info>
%   stem-info is (ideal_y shortest_y), lily/stem.cc:1123-1133 — the pair the quanter fits.
%
% A is the corpus regime: test/beaming's mixedBeams bar, whose two beams a round trip of the
% fixture measured at (0.19 . 0.81) and (2.19 . 2.81) against Lily#'s 0.19/1.00 and 2.00/2.81
% — one END each, i.e. a defect in the SLOPE and not in the height. B is LilyPond's own named
% case, the smallest group in which the counts differ at all. C is B's control and its IDENTITY
% PAIR: the same two-stem group with the counts made equal, which LilyPond answers flat for a
% reason that has nothing to do with a maximum — there is no maximum to take. The LP-side
% difference between B and C is therefore ZERO, and whatever Lily# puts between them is the
% defect itself (HANDOFF 5.0: the strongest pair is the one LilyPond answers identically).
\paper { indent = 0 ragged-right = ##t }

#(define (dump-beam name)
   (lambda (grob)
     (let ((stems (ly:grob-array->list (ly:grob-object grob 'stems))))
       (format #t "PROBEMC ~a BEAM stems=~a pos=~a dirs=~a info=~a\n" name
               (length stems)
               (ly:grob-property grob 'positions)
               (map (lambda (s) (ly:grob-property s 'direction)) stems)
               (map (lambda (s) (ly:grob-property s 'stem-info)) stems)))))

sweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with { \override Beam.after-line-breaking = #(dump-beam name) }
      { $music } #})

% test/beaming, phrase mixedBeams. `lysc ly` exports it as
%   \relative c' { c8 d16 e f8 g16 a g8 f16 e d4 }
% which is the absolute music written out below.
\score { \sweep "A" { \time 4/4 c'8 d'16 e' f'8 g'16 a' g'8 f'16 e' d'4 } }

% LilyPond's own example (stem.cc:1200): must be horizontal.
\score { \sweep "B" { \time 4/4 a''8[ a''32] r16. r4 r2 } }

% ...and the same two stems with their counts made equal.
\score { \sweep "C" { \time 4/4 a''8[ a''8] r4 r2 } }

% The SECOND defect the pair above exposed: test/beamlet-peaks holds this rhythm twice,
% a third apart, and porting the maximum closed the upper one exactly while the lower one
% did not move at all (-3.50 . -2.19 against LilyPond's -4.00 . -2.81). LilyPond answers
% the two with the SAME dy 1.19 and a height one third (1.0 ss) apart, i.e. it translates
% them; so the LP side of this pair is identity in SLOPE, and whatever Lily# puts between
% their slopes is the defect. D is the one that is already exact and must stay so.
% ⚠️ An octave up from the fixture's own spelling: test/beamlet-peaks is `octave absolute`,
% where Lily#'s bare `c` is LilyPond's `c'`, so its `c'8[ e'32 g'8]` is `c''8[ e''32 g''8]`
% here. Written at the .lys octave these two books answer (1.19 . 2.19) and (0.81 . 1.19)
% with the stems UP — a different regime, and not the one the fixture is in.
\score { \sweep "D" { \time 4/4 c''8[ e''32 g''8] r8 r16. r2 } }
\score { \sweep "E" { \time 4/4 a'8[ c''32 e''8] r8 r16. r2 } }
