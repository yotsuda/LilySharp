\version "2.26.0"
%
% WHICH BOOKS DOES THE COVERED *STEM* SUPPLY DECIDE?
%
% beam-quanting.cc:401-418 books a second collision per covered grob: the grob's `stem`
% object, as an interval that starts at Stem::chord_start_y and runs to INFINITY in the
% stem's direction, weighted by STEM_COLLISION_FACTOR (0.1) — or by 1.0 when that stem
% carries no beam of its own.  Lily# supplies none of it.  Before porting it, this probe
% answers the only question that matters: WHERE does it change the answer?
%
% ⚠️ It cannot be answered by reading the code.  For a beam on the far side of a covered
% head the stem interval is strictly DOMINATED by that head's own box (the interval starts
% at the head's CENTRE, the box reaches half a space further), and for a beam on the stem's
% side it is a flat maximum penalty that every candidate pays alike — and a constant cannot
% change a ranking.  It decides only where the candidates STRADDLE chord_start_y.
%
% METHOD — an LP-side IDENTITY pair.  Each case is scored twice, once with the detail at
% the 0.1 the Beam grob declares and once at 0.  Same file, same music, same run: a
% difference between "cN" and "cNz" IS the beamed half of the supply with nothing else
% mixed in.  (The unbeamed half is hard-coded to 1.0 at :415-416 and no override reaches
% it; qB below is dumped for comparison against Lily# instead of perturbed.)
%
% RESULT.  Cases A-P are LilyPond's own input/regression/beam-collision-opposite-stem.ly,
% the file its author wrote for "meshing stems in oppositely directed beams".  SIX of the
% sixteen move when the supply is switched off — E (-2.0 -> 1.19), F (-2.81 -> 1.0),
% G (-3.0 -> 0.19), H and I (-5.0 -> 0.0) and N (7.81/7.19 -> 4.19/3.5).  So it is
% load-bearing, by whole staff spaces, and only in the meshing regime.
%
% ⚠️ WHERE IT DOES NOT BITE, measured and not guessed, so the next reader does not repeat
% it: a beam over a whole note (no normal stem — beam-over-other-voice.ly), a beam whose
% covered head sits at the very EDGE of its x span (the stem's x then falls outside the
% drawn segments and beam_y_ comes back empty), and the ten other cases of A-P.
%
% Output: PROBEB <name> dir=<beam direction> pos=<positions> n=<covered grobs>
%         cover=(<name>@<staff-position>/<stem dir><B=beamed|F=free> ...)

\paper { indent = 0 ragged-right = ##t }

#(define (cover-tag g)
   (let* ((s (ly:grob-object g 'stem #f))
          (has (and (ly:grob? s)
                    (>= (ly:grob-property s 'duration-log 0) 1))))
     (string-append
      (symbol->string (grob::name g))
      (if (grob::has-interface g 'note-head-interface)
          (format #f "@~a" (ly:grob-property g 'staff-position 0))
          "")
      (if has
          (format #f "/~a~a"
                  (if (positive? (ly:grob-property s 'direction 0)) "u" "d")
                  (if (ly:grob? (ly:grob-object s 'beam #f)) "B" "F"))
          ""))))

#(define (dump-beam name)
   (lambda (grob)
     (let* ((cg (ly:grob-object grob 'covered-grobs #f))
            (gs (if (ly:grob-array? cg) (ly:grob-array->list cg) '())))
       (format #t "\nPROBEB ~a dir=~a pos=~a n=~a cover=(~a)\n"
               name
               (ly:grob-property grob 'direction)
               (ly:grob-property grob 'positions)
               (length gs)
               (string-join (map cover-tag gs) " ")))))

sweep =
#(define-music-function (name factor music) (string? number? ly:music?)
   #{
     \new Staff \with {
       \override Beam.after-line-breaking = #(dump-beam name)
       \override Beam.details.stem-collision-factor = #factor
     } $music
   #})

% ---- LilyPond's own meshing-stem regression, verbatim ----------------------
cA = \relative { << { s16 e''16 [ s cis ] } \\ { b'16 [ s b ] } >> }
cB = \relative { << { s16 e''16 [ s cis, ] } \\ { b''16 [ s b ] } >> }
cC = \relative { << { s16 d''16 [ s cis ] } \\ { b'16 [ s b ] } >> }
cD = \relative { << { s16 c''16 [ s cis ] } \\ { b'16 [ s b ] } >> }
cE = \relative { << { s16 b'16 [ s cis ] } \\ { b'16 [ s b ] } >> }
cF = \relative { << { s16 a'16 [ s cis ] } \\ { b'16 [ s b ] } >> }
cG = \relative { << { s16 g'16 [ s cis ] } \\ { b'16 [ s b ] } >> }
cH = \relative { << { s16 c'16 [ s cis' ] } \\ { b'16 [ s b ] } >> }
cI = \relative { << { s16 c'16 [ s cis'' ] } \\ { b16 [ s b ] } >> }
cJ = \relative { << { s16 f'16 [ s cis ] } \\ { b'16 [ s b ] } >> }
cK = \relative { << { s16 e'16 [ s cis ] } \\ { b'16 [ s b ] } >> }
cL = \relative { << { s16 d'16 [ s cis ] } \\ { b'16 [ s b ] } >> }
cM = \relative { << { s16 e''16 [ s cis ] } \\ { b'16 [ s d ] } >> }
cN = \relative { << { s16 e''16 [ s cis ] } \\ { b'16 [ s f' ] } >> }
cO = \relative { << { s16 e''16 [ s cis ] } \\ { b'16 [ s a ] } >> }
cP = \relative { << { s16 e''16 [ s cis ] } \\ { b'16 [ s gis ] } >> }

\score { \sweep "cA"  #0.1 \cA } \score { \sweep "cAz" #0 \cA }
\score { \sweep "cB"  #0.1 \cB } \score { \sweep "cBz" #0 \cB }
\score { \sweep "cC"  #0.1 \cC } \score { \sweep "cCz" #0 \cC }
\score { \sweep "cD"  #0.1 \cD } \score { \sweep "cDz" #0 \cD }
\score { \sweep "cE"  #0.1 \cE } \score { \sweep "cEz" #0 \cE }
\score { \sweep "cF"  #0.1 \cF } \score { \sweep "cFz" #0 \cF }
\score { \sweep "cG"  #0.1 \cG } \score { \sweep "cGz" #0 \cG }
\score { \sweep "cH"  #0.1 \cH } \score { \sweep "cHz" #0 \cH }
\score { \sweep "cI"  #0.1 \cI } \score { \sweep "cIz" #0 \cI }
\score { \sweep "cJ"  #0.1 \cJ } \score { \sweep "cJz" #0 \cJ }
\score { \sweep "cK"  #0.1 \cK } \score { \sweep "cKz" #0 \cK }
\score { \sweep "cL"  #0.1 \cL } \score { \sweep "cLz" #0 \cL }
\score { \sweep "cM"  #0.1 \cM } \score { \sweep "cMz" #0 \cM }
\score { \sweep "cN"  #0.1 \cN } \score { \sweep "cNz" #0 \cN }
\score { \sweep "cO"  #0.1 \cO } \score { \sweep "cOz" #0 \cO }
\score { \sweep "cP"  #0.1 \cP } \score { \sweep "cPz" #0 \cP }

% ---- the three books the ledger reads --------------------------------------
% A-P are written with manual brackets over skips and end mid-measure, so Lily# cannot be
% handed the same music.  These three are the same MECHANISM in music both engines group
% identically, and they are a fork: qA can only be explained by the infinite interval,
% qB can also be explained by booking the covered Stem's own box, qC says neither is a
% floor.
%
% ⚠️ Lily# ends an eighth beam at every QUARTER while LilyPond ends it at the half-measure
% in 4/4, so a group of eighths offset from the beat is not the same music on the two
% sides (HANDOFF 5.0, trap 5's family).  Sixteenths are grouped per quarter by both.  So
% the MEASURED beam is two eighths filling beat one — ONE beam line, unambiguously the
% primary — and the covering voice is sixteenths inside that same beat.
%
% ⚠️ The covered head must sit STRICTLY INSIDE the measured beam's x span.  At the very
% edge the head is still booked, but its stem's x falls outside the drawn segments, so
% add_collision's beam_y_ comes back empty and the supply measures nothing.
%
%   qA  covered stem is BEAMED  -> weight 0.1.  Its Stem grob is NOT a covered grob at all
%       (beam-collision-engraver.cc:179-181 drops beamed stems), so nothing but the
%       interval at :401-418 can account for the reading.
%   qB  covered stem is FREE    -> weight 1.0, hard-coded at :415-416.  Such a stem IS a
%       covered grob in its own right, and its box already spans head to tip.
%   qC  CONTROL, nothing overhead.  3.0 is where this beam sits when nobody reaches it,
%       which is what says qA and qB are not sitting on a floor.
qA = << { b'8 b' s2. }  \\ { s16 d'''16 d''' d''' s2. } >>
qB = << { b'8 b' s2. }  \\ { s16 d'''4 s8. s2 } >>
qC = << { b'8 b' s2. }  \\ { s1 } >>

\score { \sweep "qA" #0.1 \qA } \score { \sweep "qAz" #0 \qA }
\score { \sweep "qB" #0.1 \qB } \score { \sweep "qBz" #0 \qB }
\score { \sweep "qC" #0.1 \qC }
