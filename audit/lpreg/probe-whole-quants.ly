\version "2.16.0"
\paper { indent = 0 ragged-right = ##t }
\layout { debug-beam-scoring = ##t }
\relative c'''{
  \override Beam.inspect-quants = #'(-0.19 . -0.19)
  \repeat tremolo 32{ g64 a }
}
