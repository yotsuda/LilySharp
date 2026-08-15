\version "2.24.0"
% tuplet-bracket-direction twin (LP side) — verbatim music, paper aligned.
\paper { indent = 0 ragged-right = ##t }
\relative c'' {
  \tuplet 3/2 { r r r }
  \tuplet 3/2 { r c r }
  \tuplet 3/2 { r a r }
  \tuplet 3/2 { c' f,, r }
  \tuplet 3/2 { f, c'' r }
  \tuplet 3/2 { a a c }
  \tuplet 3/2 { c c a }
  \tuplet 3/2 { a a a }
  \tuplet 3/2 { c c c }
}
