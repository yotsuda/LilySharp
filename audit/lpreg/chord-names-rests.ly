\version "2.23.10"
\paper { indent = 0 ragged-right = ##t }
mus = \chordmode { r1 s1 R1 }
<<
  \new ChordNames \mus
  \mus
>>
