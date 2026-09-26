\version "2.26.0"
%
% THE LINE-START `.|:` BEFORE A FULL TAB'S DOWN STEM — the optical correction of
% lily/staff-spacing.cc:43-67 read at a system's start, which Lily# prices in LineStartColumn
% rather than with the mid-line bar (tab-stem-spacing.ly). Systems 2 and 3 both open on
% `.|:`; system 2's first quarter is on string 1 (stem DOWN), system 3's on string 5 (UP), so
% the difference of their first columns' X is the correction alone.
% Read stdout alone (`> out 2> err`), as tab-stem-spacing.ly says.
% MEASURED on 2.26.0 (session 635): first column 7.468571 (system 2) and 7.240000 (system 3),
% a difference of 0.228571 = min (4.0 / 7, 1) * 0.4 — the stem's (-1.5, 2.899) in page units
% against the bar's +-2.5 tab spaces.
\paper { indent = 0 ragged-right = ##t }
gtr = \fixed c' {
  \time 4/4
  c4\5 c\5 c\5 c\5 | \break
  \repeat volta 2 { e'4\1 e'\1 e'\1 e'\1 | }
  \break
  \repeat volta 2 { c4\5 c\5 c\5 c\5 | }
}
\score {
  \new TabStaff \with { stringTunings = #guitar-tuning }
    { \tabFullNotation
      \override TabStaff.Stem.stencil =
        #(lambda (grob)
           (let* ((sys (ly:grob-system grob)))
             (format #t "\nPROBEX ~a ~a\n" (ly:grob-property (ly:item-get-column grob) 'when) (ly:grob-relative-coordinate (ly:item-get-column grob) sys X)))
           (tabvoice::draw-double-stem-for-half-notes grob))
      \override TabStaff.BarLine.stencil =
        #(lambda (grob)
           (let* ((sys (ly:grob-system grob)) (ext (if (ly:grob? sys) (ly:grob-extent grob sys X) (cons 0 0))))
             (format #t "\nPROBEBAR ~a ~a ~a\n" (ly:grob-property grob 'glyph-name) (car ext) (cdr ext)))
           (ly:bar-line::print grob))
      \transpose c c, \gtr }
  \layout {}
}
