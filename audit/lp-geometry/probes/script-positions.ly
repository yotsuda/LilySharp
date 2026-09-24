\version "2.26.0"

%% WHERE A SCRIPT STANDS, POSITION BY POSITION (HANDOFF R10(a), session 574).
%%
%% Staccato and tenuto on every note from g to d''' (ledger lines both sides), each on the
%% head side (the default direction opposite the stem), plus a slurred run. The review
%% named three aligned_side spellings in ArticulationEngraver and the on_line ledger parity
%% (script-interface / side-position quantize); this book asks LilyPond where each one ends.
%%
%% Dump: label, note staff-position, script direction, script Y-offset over the staff middle.

#(define (dump-script label)
   (lambda (g)
     (let* ((sys (ly:grob-system g))
            (head (ly:grob-parent g X))
            (staff (ly:grob-object g 'staff-symbol))
            (mid (if (ly:grob? staff) (ly:grob-relative-coordinate staff sys Y) 0))
            (y (- (ly:grob-relative-coordinate g sys Y) mid))
            (col (ly:grob-parent g X))
            (heads (ly:grob-object col 'note-heads))
            (pos (if (ly:grob-array? heads)
                     (ly:grob-property (ly:grob-array-ref heads 0) 'staff-position)
                     'none)))
       (format #t "\nSCRIPT ~a pos=~a dir=~a y=~a\n" label pos
               (ly:grob-property g 'direction) y))))

probe = #(define-music-function (label music) (string? ly:music?)
  #{ \new Staff \with {
       \override Script.after-line-breaking = #(dump-script label)
     } { \clef "treble" \time 4/4 #music } #})

\score { \probe "SPS" {
  g4-. a-. b-. c'-. | d'-. e'-. f'-. g'-. | a'-. b'-. c''-. d''-. |
  e''-. f''-. g''-. a''-. | b''-. c'''-. d'''-. r | } \layout { indent = 0 } }

\score { \probe "SPT" {
  g4-- a-- b-- c'-- | d'-- e'-- f'-- g'-- | a'-- b'-- c''-- d''-- |
  e''-- f''-- g''-- a''-- | b''-- c'''-- d'''-- r | } \layout { indent = 0 } }

\score { \probe "SPU" {
  g4^. a^. b^. c'^. | d'^. e'^. f'^. g'^. | a'^. b'^. c''^. d''^. |
  e''^. f''^. g''^. a''^. | b''^. c'''^. d'''^. r | } \layout { indent = 0 } }

\score { \probe "SPL" {
  c'8-.( d'-. e'-. f'-.) g'-.( a'-. b'-. c''-.) | c''4-.( b'-. a'-. g'-.) | } \layout { indent = 0 } }
