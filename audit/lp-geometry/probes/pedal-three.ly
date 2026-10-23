\version "2.26.0"
% Which pedal family lands NEAREST the staff when all three are struck at once?
% One score per pedal so every label can be attributed, then all three together.
\paper { indent = 0 ragged-right = ##t line-width = 60 }

\score { \new Staff \with { pedalSustainStyle = #'text pedalSostenutoStyle = #'text }
  { \clef bass c1\sustainOn | c1\sustainOff | } \layout {} }

\score { \new Staff \with { pedalSustainStyle = #'text pedalSostenutoStyle = #'text }
  { \clef bass c1\sostenutoOn | c1\sostenutoOff | } \layout {} }

\score { \new Staff \with { pedalSustainStyle = #'text pedalSostenutoStyle = #'text }
  { \clef bass c1\unaCorda | c1\treCorde | } \layout {} }

\score { \new Staff \with { pedalSustainStyle = #'text pedalSostenutoStyle = #'text }
  { \clef bass c1\sustainOn\sostenutoOn\unaCorda | c1\sustainOff\sostenutoOff\treCorde | }
  \layout {} }

% -----------------------------------------------------------------------------------------
% WHAT THIS FILE FOUND (2026-08-18, session 206)
%
% Read off the drawn SVG (lilypond -dbackend=svg), distance from the staff's BOTTOM line to
% each family's row, on the fourth score where all three are struck together:
%
%   una corda   58.4681 - 55.6906 = 2.777500     <- NEAREST the staff
%   sostenuto   60.4293 - 55.6906 = 4.738700
%   sustain     62.8719 - 55.6906 = 7.181300     <- outermost
%
% ⇒ THE ORDER IS una corda, sostenuto, sustain. Lily# had ranked una corda OUTERMOST, which
%   its own comment called a guess and marked as one; every family on a three-pedal book sat
%   one row wrong. Fixed in MusicMarkEngraver.PedalFamilyRank.
%
% ⇒ THE INSTRUMENT REPRODUCED A NUMBER IT DID NOT KNOW: sustain - sostenuto = 2.442600,
%   against the 2.443 session 204 measured from the sustain/sostenuto PAIR alone. That
%   agreement is what says this is a reading of the engine and not of the probe.
%
% ⚠️ THE SUSTAIN ROW IS NOT TEXT. LilyPond draws "Ped." with Emmentaler glyphs
%   (lily/sustain-pedal.cc), so scores 1 and 4 print no <tspan> for it and a probe that scans
%   text alone reports two rows and misses the third in silence (HANDOFF §5.3). Its row is
%   read from the glyph's own translate instead. The three single-pedal scores exist to
%   attribute every label: without them "Sost. Ped." and "*" cannot be told apart from the
%   sustain pair, and score 2 is what shows the "*" belongs to SOSTENUTO.
%
% ⚠️ THE STEPS (1.961 then 2.443, each row's own ink) were ported two days later, 2026-08-20
%   (82c72f64a): each word hangs from its own LineSpanner and same-column words stack by their
%   ink at 0.46 — ledger mark.pedal.rows.{una-corda,sostenuto,sustain} over pedal-rows.ly,
%   the 9-digit dump twin of this book (this file's SVG read was a line-thickness off).
%   Until then Lily# used one StackGap (2.46) for both and only the ORDER was ported here.