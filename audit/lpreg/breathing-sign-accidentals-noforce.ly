\version "2.23.3"

% Frame-aligned twin of input/regression/breathing-sign-accidentals.ly:
% the forced accidentals e!/g! are dropped because Lily# cannot write them
% (LYS4009); both sides of the comparison lose the same naturals.

\relative c'' {
  f2 \breathe <g bis> |
  g2 \breathe <e g> |
  f2 \breathe <g bes> |
  g2 \breathe <g ees> |
  f2 \breathe <g bis> |
  g2 \breathe <e g> |
}
