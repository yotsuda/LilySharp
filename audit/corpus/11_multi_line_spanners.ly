\version "2.24.4"
% Multi-line Spanner Test
% Fixture for K-1 break-substitution: slur, tie, hairpin all crossing the
% forced line break so that LP and Lily# can be visually compared.
% LILYPOND-REF: lily/break-substitution.cc, lily/spanner.cc::do_break_processing
\paper {
  ragged-right = ##f
  paper-width = 100\mm
  paper-height = 200\mm
  top-margin = 5\mm
  bottom-margin = 5\mm
  left-margin = 5\mm
  right-margin = 5\mm
}
\score {
  \new Staff \relative c' {
    \time 4/4
    \clef treble
    \key c \major
    % Slur across the line break (m1 → m2)
    c4( d e f | g4 a b c) \break
    % Tie across the line break (m3 ends with ~, m4 starts with the held note)
    d2 e2~ | e4 f g a \break
    % Hairpin across the line break (\< from m5, ends at \! in m6)
    c4\< d e f | g4 a b c\! \bar "|."
  }
  \layout {}
}
