\version "2.26.0"
%
% How far does LilyPond lift a beam that runs over a note SUSTAINED IN ANOTHER VOICE?
%
% The music is test/multivoice-beam-collision: voice one beams eight C5 eighths with
% stems up, voice two holds an A5 whole note right under the beam's path. LilyPond's
% Beam_collision_engraver hands the beam that head as a covered grob, so the quanter
% must raise the beam clear of it.
%
% A = the real case.  B = the CONTROL: the same beamed eighths alone. B is the height
% the beam takes with nothing overhead, so A - B is what the other voice's head costs.
%
% ⚠️ Pitches: Lily#'s `octave absolute` sits an octave above LilyPond's, so the
% fixture's c' / a' are c'' / a'' here.

\paper { indent = 0 }

#(define (dump-beam name)
   (lambda (grob)
     (format #t "\n~a BEAM positions=~a X-positions=~a\n" name
             (ly:grob-property grob 'positions)
             (ly:grob-property grob 'X-positions))))

% ---- A: the other voice's head is under the beam ---------------------------
\score {
  \new Staff <<
    \new Voice { \voiceOne \override Beam.after-line-breaking = #(dump-beam "A")
                 c''8 c'' c'' c'' c'' c'' c'' c'' }
    \new Voice { \voiceTwo a''1 }
  >>
  \header { piece = "A" }
}

% ---- B: control, one voice only --------------------------------------------
\score {
  \new Staff \new Voice {
    \voiceOne \override Beam.after-line-breaking = #(dump-beam "B")
    c''8 c'' c'' c'' c'' c'' c'' c''
  }
  \header { piece = "B" }
}
