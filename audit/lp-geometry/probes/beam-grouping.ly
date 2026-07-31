\version "2.26.0"
%
% WHERE DOES AN AUTOMATIC BEAM END?
%
% LilyPond ends a beam at the ends of the BEATS its meter declares, and a meter's beats are
% beatBase (one over the denominator) times beatStructure — a list that is (1 1 1 ...) for
% most meters, (3 3 ...) when the numerator is over three and divisible by three, and an
% explicit uneven list for the three meters whose table entry overrides that
% (scm/time-signature-settings.scm:125-171 default-time-signature-settings:
% 4/8 as 2+2, 5/8 as 3+2, 8/8 as 3+3+2). On top of that sit beamExceptions, which beam
% eighths BEYOND the beat in a few meters (4/4 by half measure, 3/4 and 2/8 and 3/8 by whole
% measure).
%
% Lily# has a second, flatter spelling of the same grid (BeamDetector's beatLength): the
% dotted quarter for compound meters and one over the denominator otherwise. That agrees with
% LilyPond for 4/4, 3/4, 2/4, 6/8, 9/8, 12/8 and disagrees for every x/8 meter above — and
% since one eighth per group leaves each group holding a single note, the disagreement is not
% a differently-placed beam but NO BEAM AT ALL.
%
% Every score here is plain eighths filling one bar, with no manual bracket anywhere, so the
% grouping is the only thing under test. The controls are as important as the rest: a change
% to Lily#'s grid that moves 4/4 or 6/8 has broken something LilyPond agrees with today.
%
% Output: PROBEB <name> BEAM stems=<how many stems this beam joins>
%   in left-to-right order, so the LIST of them is the bar's grouping.

\paper { indent = 0 ragged-right = ##t }

#(define (dump-beam name)
   (lambda (grob)
     (format #t "PROBEB ~a BEAM stems=~a\n" name
             (ly:grob-array-length (ly:grob-object grob 'stems)))))

sweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with { \override Beam.after-line-breaking = #(dump-beam name) }
      { $music } #})

% --- the three meters whose beatStructure is an uneven table entry
\score { \sweep "M48" { \time 4/8 c'8 c' c' c' } }
\score { \sweep "M58" { \time 5/8 c'8 c' c' c' c' } }
\score { \sweep "M88" { \time 8/8 c'8 c' c' c' c' c' c' c' } }

% --- the two meters whose whole-measure beaming comes from a beamException instead
\score { \sweep "M28" { \time 2/8 c'8 c' } }
\score { \sweep "M38" { \time 3/8 c'8 c' c' } }

% --- CONTROLS: meters where Lily#'s flat grid already agrees. These must not move.
\score { \sweep "C44" { \time 4/4 c'8 c' c' c' c' c' c' c' } }
\score { \sweep "C34" { \time 3/4 c'8 c' c' c' c' c' } }
\score { \sweep "C24" { \time 2/4 c'8 c' c' c' } }
\score { \sweep "C68" { \time 6/8 c'8 c' c' c' c' c' } }
\score { \sweep "C98" { \time 9/8 c'8 c' c' c' c' c' c' c' c' } }
% …and one that is NOT pure eighths, so no beamException applies and the bare beat structure
% is what answers: sixteenths in 4/4 group by the quarter, not by the half measure.
\score { \sweep "C44S" { \time 4/4 c'16 c' c' c' c' c' c' c' c' c' c' c' c' c' c' c' } }

% --- a REST inside what the exception would otherwise beam as one group. 3/4 beams eighths
% by the whole measure, and this bar's six slots hold two runs of eighths on either side of a
% rest. The question is whether "beam eighths by the measure" reaches across the rest.
% ⚠️ 3/4 is the only meter where this can be asked of eighths at all: an exception group of
% four (4/4's half measure) cannot hold two runs of two AND a rest.
\score { \sweep "M34R" { \time 3/4 c'8 c' r8 c' c' c' } }

% --- three eighths inside ONE exception group, but split unevenly by the BEATS underneath:
% the half measure from 1/2 holds eighths at 1/2, 5/8 and 3/4, and the beat boundary at 3/4
% falls between the second and the third. The exception says beam all three; a first pass
% that groups by the beat and then throws away any group holding a single note loses the last
% one before the exception is ever consulted.
\score { \sweep "M44B" { \time 4/4 c'2 c'8 c' c' r8 } }

% --- the beamExceptions that are NOT keyed on an eighth. 3/4 and 4/4 each carry a second
% entry at 1/12 (scm/time-signature-settings.scm:100 and :121), whose own comment says what
% it is for: "Anything shorter by beat … we set triplets back to every beat". Without it the
% 1/8 entry would reach a run of triplet eighths and beam a whole 3/4 measure as nine notes.
% ⚠️ A tuplet's notes are looked up by their ACTUAL length, so an eighth inside a 3/2 tuplet
% is 1/12 (the file's own header says so). The grouping the entry asks for — threes of a
% twelfth — is the quarter, i.e. the same as the bare beat structure; what the entry changes
% is only that the EIGHTH entry does not apply.
\score { \sweep "T34" { \time 3/4 \tuplet 3/2 { c'8 c' c' } \tuplet 3/2 { c'8 c' c' }
                        \tuplet 3/2 { c'8 c' c' } } }
\score { \sweep "T44" { \time 4/4 \tuplet 3/2 { c'8 c' c' } \tuplet 3/2 { c'8 c' c' }
                        \tuplet 3/2 { c'8 c' c' } \tuplet 3/2 { c'8 c' c' } } }
% …and the mixed bars, which say whether a beam is broken at the tuplet's edge at all.
\score { \sweep "T34M" { \time 3/4 \tuplet 3/2 { c'8 c' c' } c'8 c' c' c' } }
\score { \sweep "T44M" { \time 4/4 \tuplet 3/2 { c'8 c' c' } \tuplet 3/2 { c'8 c' c' }
                         c'8 c' c' c' } }
% CONTROL: sixteenths inside the same tuplet are 1/24, for which NO entry exists, so the
% bare beat structure answers and they group by the quarter rather than by the tuplet.
\score { \sweep "T44S" { \time 4/4 \tuplet 3/2 { c'16 c' c' } \tuplet 3/2 { c'16 c' c' }
                         \tuplet 3/2 { c'16 c' c' } \tuplet 3/2 { c'16 c' c' }
                         \tuplet 3/2 { c'16 c' c' } \tuplet 3/2 { c'16 c' c' }
                         \tuplet 3/2 { c'16 c' c' } \tuplet 3/2 { c'16 c' c' } } }
