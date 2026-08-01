\version "2.26.0"
%
% How far does a `voice { } voice { }` span reach? Does it touch the stems of a
% measure it does not cover?
%
% LilyPond's `\\` wraps each sublist in its OWN Voice context and puts
% make-voice-props-set at its head (scm/music-functions.scm:1042-1057
% voicify-sublist), so \voiceOne/\voiceTwo live and die with the span: the
% music before and after it belongs to the surrounding implicit Voice and
% keeps the pitch-derived direction.
%
% A = the beamed measure with a `<< \\ >>` span in the SAME music variable,
%     one bar later.  B = the CONTROL: the same beamed measure alone.
%
% A and B must print the SAME positions. Until 2026-08-01 Lily# asked
% `Voices.Length > 1` — a PART-wide question — so ONE span anywhere in a part
% pinned every bar of voice 1 stem-up, and A came out a beam's width above B.
%
% ⚠️ Both bodies are what `lysc ly` emitted for the .lys probes BVS / BVSC
% (LpGeometryProbes.cs) — not hand-written. Lily#'s `octave absolute` sits an
% octave below LilyPond's, which is what `\fixed c'` carries.

\paper { indent = 0 }

#(define (dump-beam name)
   (lambda (grob)
     (format #t "\n~a BEAM positions=~a X-positions=~a\n" name
             (ly:grob-property grob 'positions)
             (ly:grob-property grob 'X-positions))))

% ---- A: a `<< \\ >>` span one bar after the beam ---------------------------
mA = \fixed c' {
  \time 4/4
  \key c \major
  g'8 a' b' c'' d'' e'' fis'' g'' |
  << { b2 a } \\ { d2 e } >>
}

\score {
  \new Staff { \clef treble \mA }
  \header { piece = "A" }
  \layout {
    \context { \Score \override Beam.after-line-breaking = #(dump-beam "A") }
  }
}

% ---- B: control, the beamed measure with no span anywhere -------------------
mB = \fixed c' {
  \time 4/4
  \key c \major
  g'8 a' b' c'' d'' e'' fis'' g'' |
}

\score {
  \new Staff { \clef treble \mB }
  \header { piece = "B" }
  \layout {
    \context { \Score \override Beam.after-line-breaking = #(dump-beam "B") }
  }
}
