\version "2.23.12"

% Frame-aligned twin of input/regression/script-stack-order1.ly, score 2.
% Score 1 ({ d''16\3-3 d'\5-4 } = string number over fingering) has no Lily#
% spelling for a standard-staff string number -> omitted on BOTH sides; the
% ladder claim lives in score 2 (7 of the 8 script kinds).

\paper { indent = 0 ragged-right = ##t }

\relative {
  e''4->-0\downbow ( c4-.) d--\downbow d,-.-0->
  r4
  a-.--\upbow f''-.---3\upbow e'\flageolet\fermata\upbow
  e,---0\downbow^"div." \bar "|."
}
