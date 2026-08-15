\version "2.19.21"

% Frame-aligned twin of input/regression/chord-names-in-grand-staff.ly.

\paper { indent = 0 ragged-right = ##t }

\score {
   \new GrandStaff
   <<
    \chords {
      f1
    }
    \new Staff {
      \relative {
        a'4 a a a
      }
    }
    \new Staff {
      \clef "bass"
      \relative {
        a,4 a a a
      }
    }
   >>
}
