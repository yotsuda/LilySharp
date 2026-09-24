\version "2.26.0"

%% A SINGLE-NOTE TREMOLO ON A BEAMED STEM (HANDOFF R9(g), session 574).
%%
%% Two halves of one grob: (1) Stem::calc_stem_info reserves height_of_my_trem =
%% Stem_tremolo::vertical_length + beam_translation (lily/stem.cc:1187-1211, :1256), which
%% moves the beam; (2) Stem_tremolo::y_offset hangs the slash stack
%% max(beam_count,1)*beam_translation inside the stem's beam end (stem-tremolo.cc:313-369),
%% at the beam's slope, 1.0 wide, a rotated rectangle. Lily# had neither until then: the
%% beam ignored the tremolo and the slashes were not drawn at all.
%%
%% Dumps: the beam's quantized positions (staff spaces over the middle line), and per
%% tremolo its stem's head position, the stack's Y-offset over the staff middle, its slope
%% and its drawn Y extent.

#(define (dump-beam label)
   (lambda (g)
     (format #t "\nBEAM ~a pos=~a\n" label (ly:grob-property g 'quantized-positions))))

#(define (dump-trem label)
   (lambda (g)
     (let* ((stem (ly:grob-object g 'stem))
            (sys (ly:grob-system g))
            (staff (ly:grob-object stem 'staff-symbol))
            (mid (ly:grob-relative-coordinate staff sys Y))
            (y (- (ly:grob-relative-coordinate g sys Y) mid))
            (ext (coord-translate (ly:grob-extent g sys Y) (- mid)))
            (sx (coord-translate (ly:grob-extent stem sys Y) (- mid))))
       (format #t "\nTREM ~a y=~a ext=~a slope=~a flags=~a stem=~a stemx=~a\n" label y ext
               (ly:grob-property g 'slope) (ly:grob-property g 'flag-count) sx
               (ly:grob-relative-coordinate stem sys X)))))

probe = #(define-music-function (label music) (string? ly:music?)
  #{ \new Staff \with {
       \override Beam.after-line-breaking = #(dump-beam label)
       \override StemTremolo.after-line-breaking = #(dump-trem label)
     } { \clef "treble" \time 4/4 #music } #})

\score { \probe "TRB0" { a'8:32[ a'8:32] r4 r2 } \layout { indent = 0 } }
\score { \probe "TRBC" { a'8[ a'8] r4 r2 } \layout { indent = 0 } }
\score { \probe "TRB1" { c'8:32[ e'8:32] r4 r2 } \layout { indent = 0 } }
\score { \probe "TRB2" { a''8:64[ g''8:64] r4 r2 } \layout { indent = 0 } }
\score { \probe "TRB3" { c'8:32[ g'8:32] r4 r2 } \layout { indent = 0 } }
\score { \probe "TRB4" { c'16:64[ d'16:64 e'16:64 f'16:64] r4 r2 } \layout { indent = 0 } }
