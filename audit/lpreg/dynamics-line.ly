\version "2.16.0"

% Frame-aligned twin of input/regression/dynamics-line.ly:
% verbatim body, plus the standard comparison paper (indent 0 + ragged-right)
% and \bar "|." (Lily# always draws the final barline).

\paper { indent = 0 ragged-right = ##t }

\relative c''{
  a1^\sfz
  a1\fff\> c,,\!\pp a'' a\p

  %% We need this to test if we get two Dynamic line spanners
  a

  %% because do_removal_processing ()
  %% doesn't seem to post_process elements
  d\f

  a \bar "|."
}
