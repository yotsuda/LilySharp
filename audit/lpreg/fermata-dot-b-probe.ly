\version "2.23.12"

% Which mechanism binds the fermata stacked over an accent?
% m1 = baseline (fermata-dot-b.ly's pair 2).
% m2 = outside-staff-padding 0 on the fermata -> moves iff the COLLISION PASS binds.
% m3 = side-position padding 0 on the fermata -> moves iff the ENGRAVER (support chain) binds.
% m4 = outside-staff-priority off on the fermata -> pure engraver answer (support chain only).

\paper { indent = 0 ragged-right = ##t }

\relative c''' {
  \tempo 4 = 60
  a4-> a4->\fermata
  a4-> a4->\tweak outside-staff-padding #0 \fermata
  a4-> a4->\tweak padding #0 \fermata
  a4-> a4->\tweak outside-staff-priority ##f \fermata \bar "|."
}
