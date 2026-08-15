\version "2.26.0"

% tablature.ly 逐語 + paper 揃え + \bar "|."。

\paper { ragged-right = ##t }

partition =  {
  \key e \major
  <e\5 dis'\4>
  <e dis'>
  <e dis'\4>
  <e dis'>\5\4
  <e dis'\4>\5
  \bar "|."
}

\context StaffGroup <<
  \context Staff <<
    \clef "G_8"
    \partition
  >>
  \context TabStaff <<
    \partition
  >>
>>

\layout { indent = 0\mm }
