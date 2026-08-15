\version "2.26.0"
% Where does a ONE-BAR multi-measure rest sit, on its own and in a voice?
% Asked because part-combine-silence-mixed puts one in the shared voice (bar 4) and one in
% voice two (bar 2), and Lily# drew both at +1.0 -- the position of an ordinary semibreve rest.
% lily/multi-measure-rest.cc:254-264 says a one-bar MMR is Rest::staff_position_internal(0,dir)
% MINUS 2, which is a different number from the ordinary rest at the same duration.
\include "pcdump.ily"
\paper { indent = 0 ragged-right = ##t }

% neutral (no voice settings) -- one bar of MMR, then a note so the book is not all silence
\score { \new Staff \relative f' { R1 | g1 | } }

% the same bar in voice one and in voice two, and an ordinary r1 in each for the control
\score { \new Staff << \new Voice { \voiceOne R1 | r1 | } \new Voice { \voiceTwo R1 | r1 | } >> }
