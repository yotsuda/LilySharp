\version "2.26.0"
%
% Two covered-grob supplies Lily# did not have (session 916, 2026-10-20):
%   X  - a manual beam ACROSS A BAR LINE over another voice's head. Lily# passed such a
%        beam no collisions at all.
%   OA/OF - another voice's ACCIDENTAL under a one-measure beam. Lily# booked the other
%        voices' heads and stems but not their accidentals.
% Each has its control (XC, OC, OFC): the same beam with the covered grob taken away.
%
% Output (LilyPond 2.26.0):
%   X   BEAM positions=(4.19 . 4.5)     XC  BEAM positions=(3.19 . 3.5)
%   OA  BEAM positions=(5.19 . 5.5)     OC  BEAM positions=(4.19 . 4.5)
%   OF  BEAM positions=(6.0 . 6.19)     OFC BEAM positions=(4.81 . 5.0)
%
% ⚠️ Lily#'s `octave absolute` sits an octave above LilyPond's: c'' here is c' there.

\paper { indent = 0 }

#(define (dump-beam name)
   (lambda (grob)
     (format #t "\n~a BEAM positions=~a X-positions=~a\n" name
             (ly:grob-property grob 'positions)
             (ly:grob-property grob 'X-positions))))

\score {
  \new Staff { \time 2/4
    << { \override Beam.after-line-breaking = #(dump-beam "X") r4 r8 c''8[ | d''8] r8 r4 } \\
       { r4 a''4 | a''4 r4 } >> }
  \header { piece = "X" }
}

\score {
  \new Staff { \time 2/4
    << { \override Beam.after-line-breaking = #(dump-beam "XC") r4 r8 c''8[ | d''8] r8 r4 } \\
       { s2 | s2 } >> }
  \header { piece = "XC" }
}

\score {
  \new Staff { \time 2/4
    << { \override Beam.after-line-breaking = #(dump-beam "OA") c''8[ d'' e'' d''] } \\
       { r8 ais''4 r8 } >> }
  \header { piece = "OA" }
}

\score {
  \new Staff { \time 2/4
    << { \override Beam.after-line-breaking = #(dump-beam "OC") c''8[ d'' e'' d''] } \\
       { r8 a''4 r8 } >> }
  \header { piece = "OC" }
}

\score {
  \new Staff { \time 2/4
    << { \override Beam.after-line-breaking = #(dump-beam "OF") c''8[ d'' e'' d''] } \\
       { r8 bes''4 r8 } >> }
  \header { piece = "OF" }
}

\score {
  \new Staff { \time 2/4
    << { \override Beam.after-line-breaking = #(dump-beam "OFC") c''8[ d'' e'' d''] } \\
       { r8 b''4 r8 } >> }
  \header { piece = "OFC" }
}
