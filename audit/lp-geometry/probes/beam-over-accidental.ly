\version "2.26.0"
%
% Where does LilyPond put a beam that runs over a printed accidental?
%
% The music is the one Lily# rests a beam on a sharp in: in bass clef, key A major,
% `gis,16 dis8 fis16` after `r2 r4`. Only `dis` prints an accidental (D sharp is not
% in the key), and it sits ON the middle line, directly under the beam's left end —
% under the SIXTEENTH's stub, which is the second beam line.
%
% A = the real case.  B = the CONTROL: the same rhythm and the same staff positions
% with the sharp spelled away (`d` instead of `dis`), so no accidental is printed and
% nothing but the note heads is under the beam. LilyPond's answer for B is the "no
% obstacle" height; the difference A - B is what the accidental is worth.
%
% Beam.positions is the CENTRE line of the PRIMARY beam (rank 0) in staff spaces from
% the staff's middle line, at the beam's own X-positions ends
% (lily/beam.cc:783-814 Beam::print adds beam_dy * vertical_count to it).

\paper { indent = 0 }

#(define (dump-beam name)
   (lambda (grob)
     (format #t "\n~a BEAM positions=~a X-positions=~a\n" name
             (ly:grob-property grob 'positions)
             (ly:grob-property grob 'X-positions))))

#(define (dump-acc name)
   (lambda (grob)
     (format #t "\n~a ACC staff-position=~a X=~a Y=~a\n" name
             (ly:grob-property grob 'staff-position)
             (ly:grob-extent grob (ly:grob-system grob) X)
             (ly:grob-extent grob (ly:grob-system grob) Y))))

% ---- A: the accidental is printed -----------------------------------------
\score {
  \new Staff {
    \clef bass
    \key a \major
    \override Beam.after-line-breaking = #(dump-beam "A")
    \override Accidental.after-line-breaking = #(dump-acc "A")
    r2 r4 gis,16 dis8 fis16
  }
  \header { piece = "A" }
}

% ---- B: control, no accidental --------------------------------------------
\score {
  \new Staff {
    \clef bass
    \key a \major
    \override Beam.after-line-breaking = #(dump-beam "B")
    r2 r4 gis,16 d8 fis16
  }
  \header { piece = "B" }
}
