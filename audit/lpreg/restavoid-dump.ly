\version "2.24.0"
\paper { indent = 0 }
\score {
  \new Staff <<
    \relative { g''8 g g r r2 } \\
    \relative { r4 c' r2 } \\
    \relative { c''4 c r2 } \\
    \relative { r2 g' }
  >>
  \layout {
    ragged-right = ##t
    \context { \Score
      \override Rest.after-line-breaking =
        #(lambda (g) (format #t "REST dur=~a pos=~a yoff=~a\n"
             (ly:grob-property g 'duration-log)
             (ly:grob-property g 'staff-position)
             (ly:grob-property g 'Y-offset)))
    }
  }
}
