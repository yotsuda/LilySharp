\version "2.16.0"
\paper { indent = 0 ragged-right = ##t }
\layout { debug-beam-scoring = ##t }
\relative c'''{
  \override Beam.inspect-quants = #'(1.0 . 1.0)
  \repeat tremolo 32{ g64 a }
}

