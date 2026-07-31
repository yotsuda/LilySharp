\version "2.26.0"
%
% IS A GRACE STEM'S THICKNESS SCALED?
%
% beam-grace-score.ly settled that Lily#'s grace beam is quanted to LilyPond's own
% configuration — (0.142 . 0.5), nine places — and that everything left is the projection
% from that configuration to the DRAWN quad. The projection is fixed by one number: half a
% stem thickness, which is where the beam's drawn end sits relative to its outer stem
% (lily/beam.cc:631 horizontal_[d] += d * stem_width / 2).
%
% beam-grace.ly already measured the CONSEQUENCE of that number — LilyPond's grace beam is
% drawn 0.065 outside each stem, half the UNSCALED 0.13 — but it measured it off the beam.
% This probe asks the stem itself, because the same number is spent in three places on
% Lily#'s side (the stem's x attachment, the stem's drawn width, the beam's overhang) and
% "the beam's end is at 0.065" only pins the third. If Stem.thickness really is in
% line-thickness units and fontSize does not reach it, then a grace stem is drawn the SAME
% width as a full-size one and the other two follow; if instead LilyPond scales it, then
% Lily#'s renderer is right about the width and wrong only about the beam.
%
% Output: PROBEGSF <name> STEM thick=<Stem.thickness> x=<stem refpoint x> xext=<stem X extent>
%                         head=<notehead X extent> fs=<the Voice's fontSize>
%
% WHAT IT SAID (2026-08-01, session 60): see the ledger keys grace.stem.thickness and
% grace.stem.attach-from-head-right.
\paper { indent = 0 ragged-right = ##t }

#(define (dump-stems name)
   (lambda (grob)
     (let* ((sys (ly:grob-system grob))
            (stems (ly:grob-array->list (ly:grob-object grob 'stems))))
       (for-each
        (lambda (s)
          (let ((head (ly:grob-array->list (ly:grob-object s 'note-heads))))
            (format #t "PROBEGSF ~a STEM thick=~a x=~a xext=~a head=~a fs=~a\n" name
                    (ly:grob-property s 'thickness)
                    (ly:grob-relative-coordinate s sys X)
                    (ly:relative-group-extent (list s) sys X)
                    (ly:relative-group-extent head sys X)
                    (ly:grob-property s 'font-size))))
        stems))))

sweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with { \override Beam.after-line-breaking = #(dump-stems name) }
      { $music } #})

% GSFG — the corpus regime, the same book as beam-grace.ly's score G.
\score { \sweep "GSFG" { \time 4/4 \grace { d'16 e' } f'4 g'2 r4 } }

% GSFH — the full-size control, beam-grace.ly's score H. Same two pitches, ordinary
% sixteenths: whatever differs between the two cards is the grace scaling and nothing else.
\score { \sweep "GSFH" { \time 4/4 d'16 e' r8 g'2 r4 } }
