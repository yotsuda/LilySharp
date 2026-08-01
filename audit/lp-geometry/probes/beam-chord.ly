\version "2.26.0"
%
% WHERE DOES A BEAM SIT OVER A CHORD?
%
% Every beam point in the ledger before this one is a beam over SINGLE note heads. A chord
% is a different input to the same quanter: the stem attaches at one extreme head and its
% length is reckoned from the other (lily/stem.cc:103-112 Stem::head_positions,
% :114-122 Stem::chord_start_y), so a chord beam can differ from a single-note beam over
% the same outer voice without any of the quanter's own constants being wrong.
%
% This is measure 2 of LilySharp.Tests/Fixtures/test/dense-chromatic, which a handoff had
% recorded as "the chords' stem direction is the opposite of LilyPond's (LP up, Lily#
% down)". IT IS NOT: LilyPond puts all four stems DOWN, at heads (1 3 5), (2 4 6),
% (3 5 7), (3 5 7) — the same positions and the same direction Lily# draws — and both
% engines quant the beam to (-1.81 . -1.19).
%
% ⚠️ How the misreading was possible, and the trap to avoid repeating: the fixture's FIRST
% bar holds twelve sixteenths in 4/4. Lily#'s `|` is a bar line, so bar two starts fresh;
% LilyPond's bare `|` is only a bar CHECK and does not reset the measure position, so a
% transcription that keeps the `|` leaves LilyPond beaming the four chords as TWO groups
% starting three quarters into a bar. That is a different piece of music, and comparing
% its beams against Lily#'s says nothing (HANDOFF 5.0, trap 5's family: check that both
% sides are the same music BEFORE reading a divergence off them). Score A here is the bar
% on its own, which is what Lily# renders.
%
% B is the CONTROL: the same rhythm and the same LOWER voice as single notes. Stem length
% is reckoned from the head at the stem's far end, so B is what this beam would do if the
% upper chord notes were not there — A minus B is what the chord costs.
%
% C is the OTHER half of "are both sides the same music", added 2026-07-31 after the same
% false report was reached for a second time by a second route: the .lys writes these pitches
% in RELATIVE octaves, and a transcription that resolves them one octave down puts every head
% BELOW the middle line, where LilyPond really does stem them up — which is precisely the
% reported divergence, manufactured. C asks LilyPond to resolve the relative source itself
% (\relative c' takes the nearest note to C4 for the first one, which is Lily#'s rule), and
% it must answer with the same (1 3 5) … (3 5 7) that A does. ⚠️ C's GROUPING is not
% comparable for the bar-check reason above; C is about PITCH.
%
% Output: PROBEB <name> BEAM dir=<direction> pos=<positions>
%         PROBEB <name> STEM dir=<direction> heads=<staff positions>

\paper { indent = 0 ragged-right = ##t }

#(define (dump-beam name)
   (lambda (grob)
     (format #t "\nPROBEB ~a BEAM dir=~a pos=~a\n" name
             (ly:grob-property grob 'direction)
             (ly:grob-property grob 'positions))))

#(define (dump-stem name)
   (lambda (grob)
     (let ((hs (ly:grob-object grob 'note-heads #f)))
       (format #t "PROBEB ~a STEM dir=~a heads=~a\n" name
               (ly:grob-property grob 'direction)
               (if (ly:grob-array? hs)
                   (map (lambda (h) (ly:grob-property h 'staff-position 0))
                        (ly:grob-array->list hs))
                   '())))))

% ⚠️ not `chords` / `lower`: both are LilyPond keywords.
clusters = { <cis'' e'' gis''>8 <d'' f'' aes''> <ees'' ges'' bes''> <e'' g'' b''> r2 }
bottoms  = { cis''8 d'' ees'' e'' r2 }

\score { \new Staff \with {
    \override Beam.after-line-breaking = #(dump-beam "A")
    \override Stem.after-line-breaking = #(dump-stem "A")
  } { \time 4/4 \clusters } }

\score { \new Staff \with {
    \override Beam.after-line-breaking = #(dump-beam "B")
    \override Stem.after-line-breaking = #(dump-stem "B")
  } { \time 4/4 \bottoms } }

\score { \new Staff \with {
    \override Beam.after-line-breaking = #(dump-beam "C")
    \override Stem.after-line-breaking = #(dump-stem "C")
  } { \time 4/4 \relative c' {
        cis16 d dis e f fis g gis a ais b c |
        <cis e gis>8 <d f aes> <ees ges bes> <e g b> r2 } } }

% ─────────────────────────────────────────────────────────────────────────────
% D/E, added 2026-08-01: THE SAME QUESTION WHERE THE ANSWER IS NOT MASKED.
%
% A and B agree in LilyPond AND agreed in Lily# while Lily# was reading the chord's
% arithmetic MEAN where LilyPond reads head_positions[my_dir]. They could not tell the two
% apart: those beams sit near the middle line, where stem.cc:1239 clamps the ideal Y to 0
% whichever head it started from, so both readings arrive at the same quant.
%
% D is the chord that DOES separate them: <a,, c, g,> spans the staff (heads -3, -1, +3),
% stems UP, so the beam-side head is +3 and the mean is 0 — and the beam sits high enough
% that nothing clamps. The floor alone decides it: shortest_y = 1.5 + 2.24 = 3.74 from the
% real head, 0 + 2.24 = 2.24 from the mean, and 3.81 is the first quant above 3.74.
% LilyPond charges 942.03 for the quant the mean makes legal (debug-beam-scoring with
% inspect-quants) against 5.91 for its own.
%
% E is the CONTROL, and it is the same identity B has: the chord replaced by its BEAM-SIDE
% head alone. calc_stem_info reads that one head and no other, so LilyPond must answer the
% IDENTICAL pair — measured, it does. Any difference Lily# shows across D/E is therefore a
% defect with no LilyPond-side quantity left to explain it.
%
% This is the notation staff of LilySharp.Tests/Fixtures/test/tab-beam-slope, which is where
% the divergence was found once the twin and the page finally held the same chord.
%
% ⚠️ Both bodies came out of `lysc ly`, not a hand transcription — the octaves in the .lys
% are relative to Lily#'s c, not LilyPond's, and hand-spelling them is how two earlier
% false divergences were manufactured.

bqcd  = \fixed c' { <a,, c, g,>8 a,, a,, a,, e,, f,, g,, a,, | }
bqcdc = \fixed c' { g,8       a,, a,, a,, e,, f,, g,, a,, | }

\score { \new Staff \with {
    \override Beam.after-line-breaking = #(dump-beam "D")
    \override Stem.after-line-breaking = #(dump-stem "D")
  } { \time 4/4 \key c \major \clef bass \bqcd } }

\score { \new Staff \with {
    \override Beam.after-line-breaking = #(dump-beam "E")
    \override Stem.after-line-breaking = #(dump-stem "E")
  } { \time 4/4 \key c \major \clef bass \bqcdc } }
