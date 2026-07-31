\version "2.26.0"
%
% WHERE IS A STEM'S X — and therefore what frame does the beam quanter measure in?
%
% lily/beam-quanting.cc:313-315 fills stem_xpositions_ from
%   s->relative_coordinate (common[X_AXIS], X_AXIS) - x_pos[LEFT] + x_span_
% so the whole quanting frame hangs on two things: whether a Stem's reference point is its
% NoteColumn's, and where the beam's X-positions are. A Lily# handoff once recorded that
% "a Stem declares no X-offset at all (scm/define-grobs.scm:3429-3470)" and told the next
% hand not to move the quanter off the column's x. That range ends ONE LINE above
%   scm/define-grobs.scm:3471  (X-offset . ,ly:stem::offset-callback)
% whose body is lily/stem.cc:1090-1114, comment and all: "move the stem to right of the
% notehead if it is up". This probe reads the number rather than the source.
%
% Output: PROBEX <name> BEAM Xpos=<X-positions> positions=<positions>
%         PROBEX <name> STEM dir=<d> stemX=<..> colX=<..> stemExt=<..> headExt=<..> Xoff=<..>
%
% What it prints (2.26.0): Xoff = 1.2392 for an UP stem and 0.065 for a DOWN one, always
% stemX = colX + Xoff, and the Beam's Xpos running from the first stem's LEFT edge to the
% last stem's RIGHT edge (a half stem width outside each — lily/beam.cc:631). So the frame
% is the STEMS', not the columns'. With every member pointing the same way that offset is a
% constant and cancels out of the span, the slope and the least squares alike; score D is
% the control that shows it, and only a KNEE (scores A and C) can tell the two apart.
\paper { indent = 0 ragged-right = ##t }

#(define (dump-stems name)
   (lambda (grob)
     (let ((sys (ly:grob-system grob)))
       (format #t "PROBEX ~a BEAM Xpos=~a positions=~a\n" name
               (ly:grob-property grob 'X-positions)
               (ly:grob-property grob 'positions))
       (for-each
        (lambda (s)
          (let ((col (ly:grob-parent s X))
                (heads (ly:grob-array->list (ly:grob-object s 'note-heads))))
            (format #t "PROBEX ~a STEM dir=~a stemX=~a colX=~a stemExt=~a headExt=~a Xoff=~a\n"
                    name
                    (ly:grob-property s 'direction)
                    (ly:grob-relative-coordinate s sys X)
                    (ly:grob-relative-coordinate col sys X)
                    (ly:grob-extent s sys X)
                    (if (null? heads) '() (ly:grob-extent (car heads) sys X))
                    (ly:grob-property s 'X-offset))))
        (ly:grob-array->list (ly:grob-object grob 'stems))))))

sweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with { \override Beam.after-line-breaking = #(dump-stems name) }
      { $music } #})

% A and C are the two books of beam-knee.ly, so the numbers here explain those readings.
\score { \sweep "A" { \time 4/4 c'8 c''' c' c''' r2 } }
\score { \sweep "C" { \time 4/4 c'8 c''' c' r4 r2 } }
% D: the control. Every stem up, so every Xoff is the same 1.2392 and nothing can see it.
\score { \sweep "D" { \time 4/4 c'8 e' c' e' r2 } }
% E: the kneed bar of showcase/05-special-techniques, whose stems LilyPond spaces EVENLY
% (0.065 / 2.569 / 5.074 / 7.578 in the beam's frame) where Lily# bunches the last two.
% That spacing, not the frame, is what the quanter's least squares actually disagrees on.
\score { \sweep "E" { \time 4/4 c'8 c' c' c''' c c''' c c''' } }
