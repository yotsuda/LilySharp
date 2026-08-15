\version "2.24.0"
% volta-bracket-vertical-skylines twin (LP side).
\paper { indent = 0 ragged-right = ##t }
\new Staff {
  \repeat volta 3 { r2 a''''4 r4 }
  \alternative { { r2 d''''4 r4 } { r2 d''''4 r4 } { r2 d''''4 r4 } }
  \repeat volta 3 { r2 a''''4 r4 }
  \alternative { { r2 a''''4 r4 } { r2 a''''4 r4 } { r2 a''''4 r4 } }
}
