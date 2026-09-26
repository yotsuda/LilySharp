\version "2.26.0"
%
% A FULL TAB'S COLUMN SPACING — the stem corrections a TabVoice's spacing wish carries.
%
% LilyPond's Note_spacing reads each voice's OWN stems (lily/note-spacing.cc:204-315
% stem_dir_correction): on a TabVoice under \tabFullNotation those are the tab's stems, whose
% heads are the digits' STRING positions and whose direction is the tab's rule — not the
% notated pitch. Bar 1 alternates up (string 5) and down (string 1) quarters — the
% different-direction correction and the note -> bar and bar -> note corrections; bar 2
% jumps strings with stems one way — the same-direction correction, both signs; bars 3-5
% are beamed eighths, an up group, a down group, and a run on ONE string whose pitches rise
% (no correction at all, where the notation stems would have taken one).
% Bar 6's second group is c d c on string 5 and e' on string 1: the GROUP stems down, so the
% c's own up stem is not what the f# -> c gap reads. Bar 7 is an up group on fret 4 (a digit of
% its own height, which sets where the stem begins and so how much the next, down stem
% overlaps it) against a down group whose d' on string 3 reaches farthest, so the g before the
% bar line carries the GROUP's tip.
% Every note names its string (the two engines' allocators differ).
%
% Output: PROBEX <stem X in page units>, one per column in order. ⚠️ Read stdout ALONE
% (`> out 2> err`): merged with stderr, a line splits and one X reads as empty. The gaps are what
% LilySharp.Tests/TabStemSpacingTests holds.
% MEASURED on 2.26.0 (session 634): 3.898514 3.079829 3.898514 4.598264 | 3.239171 3.739171
% 3.079829 4.788650 | 2.289171 2.539171 2.289171 2.555657 2.289171 2.289171 2.039171 3.398264 |
% 2.289171 x7 3.169693 | 2.289171 2.539171 2.289171 2.039171 2.289171 2.539171 2.289171 3.552936 |
% 2.289171 2.539171 2.289171 x4 2.539171 3.129171 | 2.289171 x3 2.557228 2.039171 2.539171 2.289171
% (a gap that crosses a bar line includes the bar).

\paper { indent = 0 ragged-right = ##t paper-width = 500\mm }  % one system: the gaps are natural lengths
gtr = \fixed c' {
  \time 4/4
  c4\5 e'\1 c\5 e'\1 |
  e'4\1 g\3 e'\1 c\5 |
  c8\5 d\5 e\4 f\4 g'\1 f'\1 e'\1 d'\2 |
  e'8\1 f'\1 g'\1 a'\1 e'\1 f'\1 g'\1 a'\1 |
  c8\5 d\5 e\4 f\4 c\5 d\5 e\4 f\4 |
  c8\5 d\5 e\4 fis\4 c\5 d\5 c\5 e'\1 |
  fis8\4 fis\4 fis\4 fis\4 g'\1 d'\3 g'\1 g'\1 |
}
\score {
  \new TabStaff \with { stringTunings = #guitar-tuning }
    % The stems' X, printed when they are DRAWN — after every column is placed. (An
    % after-line-breaking hook printed 0 for one column of bar 6.) A stem stands at its
    % column plus a constant, so the gaps are the columns'.
    { \tabFullNotation
      \override TabStaff.Stem.stencil =
        #(lambda (grob)
           (format #t "\nPROBEX ~a\n" (ly:grob-relative-coordinate grob (ly:grob-system grob) X))
           (tabvoice::draw-double-stem-for-half-notes grob))
      \transpose c c, \gtr }
  \layout {}
}
