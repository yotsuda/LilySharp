\version "2.26.0"
%% LP FIDELITY PROBE — the height of a part-combine text ("a2") over a combined staff.
%%
%% WHY THIS PROBE EXISTS (session 383). The bench's a2 stood 0.11 low in Lily#, and splitting it by
%% what decides the height gave three different residuals (scratch/p384/a2): the staff-top floor
%% −0.033, a note head above the staff −0.040, an up-stem −0.1095. LilyPond places the grob with
%%   lily/side-position-interface.cc:188-455 aligned_side —
%%     the supports are the note heads and stems the engraver acknowledged
%%     (lily/part-combine-engraver.cc:102-119), floored by the staff extent (:323-330), and the
%%     grob's own facing skyline is kept `padding` 0.5 from them (:354-370);
%% and the grob's skyline is its EXTENT BOX: CombineTextScript declares no vertical-skylines
%% (scm/define-grobs.scm:1077-1105), so lily/grob.cc:81-85 installs
%% Grob::simple_vertical_skylines_from_extents. Dumped: the DOWN skyline of "a2" is flat at the ink
%% bottom −0.033010 over the whole advance 0.0 .. 2.594891 (TextScript, which declares the stencil
%% outline at :3818, would not be flat).
%%
%% THREE SCORES, ONE REGIME EACH (bass clef, `\partCombine` of a part with itself, so "a2"):
%%   PCF  f1     — nothing above the staff: the STAFF EXTENT decides. Expect 0.05 + 0.5 + 0.033.
%%   PCH  c'2    — a head one ledger above the staff: the HEAD decides.
%%   PCS  c2     — C3, stem up ending 1.0 over the top line: the STEM decides. Expect 1.0 + 0.5 + 0.033.
%% (written `\fixed c' { f,1 }` etc., the spelling `lysc ly` gives the Lily# twins; the section
%% label "Intro" of the twins is kept — RehearsalMark stacks at 1500, after the text's 475.)
%% Lily# twins: LpGeometryProbes.cs, scores PCF / PCH / PCS.
%%
%% READING: the text's baseline up from the staff's TOP line, staff spaces.
%%   ⚠️ THE LEDGER VALUE IS THE SVG'S (-dbackend=svg, fonts pinned below): the text's
%%   <g transform="translate(x, y)"> minus the top staff line's translate, per score. The dump line
%%     PROBE <tag> A2 up=<text Y − (staff symbol Y + 2)>
%%   is read in after-line-breaking, i.e. BEFORE the outside-staff collision pass
%%   (lily/axis-group-interface.cc skyline_spacing): it is the aligned_side value. It equals the
%%   final one for PCF / PCH / PCS / PCB, where nothing pushes the text further, and NOT for PCV,
%%   whose text the pass lifts over another voice's head (dump 2.078010, svg 7.5380).

\header { tagline = ##f }

\paper {
  property-defaults.fonts.serif = "LilyPond Serif"
  property-defaults.fonts.sans = "LilyPond Sans Serif"
}

#(define (nf x)
   (cond ((not (real? x)) "?")
         (else (format #f "~,6f" x))))

#(define ((dump-a2 tag) g)
   (let* ((sys (ly:grob-system g))
          (st (ly:grob-object g 'staff-symbol))
          (ty (ly:grob-relative-coordinate g sys Y))
          (sy (if (ly:grob? st) (ly:grob-relative-coordinate st sys Y) +nan.0)))
     (format #t "\nPROBE ~a A2 up=~a\n" tag (nf (- ty (+ sy 2.0))))))

pclay =
#(define-scheme-function (tag) (string?)
   #{ \layout {
        indent = 0\mm
        \context { \Score printInitialRepeatBar = ##t }
        \context { \Staff \override CombineTextScript.after-line-breaking = #(dump-a2 tag) }
      } #})

pcfBass = \fixed c' { \mark \markup \box "Intro" f,1 | }
\score { \new Staff { \clef "bass" \partCombine \pcfBass \pcfBass } \pclay "PCF" }

pchBass = \fixed c' { \mark \markup \box "Intro" c2 c2 | }
\score { \new Staff { \clef "bass" \partCombine \pchBass \pchBass } \pclay "PCH" }

pcsBass = \fixed c' { \mark \markup \box "Intro" c,2 c,2 | }
\score { \new Staff { \clef "bass" \partCombine \pcsBass \pcsBass } \pclay "PCS" }

%% PCB (session 383 leg 4) — C3 EIGHTHS under one beam: a beamed Stem's support skyline reaches the
%% beam, 3.05 over the staff middle in the dump (scratch/p384/a2/E-beam-dump.ly), where an unbeamed
%% half note's reaches 3.0. Expect 1.05 + 0.5 + 0.033010.
pcbBass = \fixed c' { \mark \markup \box "Intro" c,8 c, c, c, c, c, c, c, | }
\score { \new Staff { \clef "bass" \partCombine \pcbBass \pcbBass } \pclay "PCB" }

%% PCV (session 383 leg 5) — the support is the label's OWN VOICE: Part_combine_engraver is consisted
%% in Voice (ly/engraver-init.ly:406), so it acknowledges only that voice's heads and stems. At 0 part
%% one sounds alone ("Solo" on its c); at 1/8 its d against part two's g' is apart, so g' goes to
%% voice Two with its head far above the staff, inside the label's advance. That head reaches the
%% label only through the outside-staff pass at outside-staff-padding 0.46; a support unioned over
%% every voice would clear it at padding 0.5 and read 0.04 higher.
pcvOne = \fixed c' { \mark \markup \box "Intro" c8 d8 r4 r2 | }
pcvTwo = \fixed c' { \mark \markup \box "Intro" r8 g'8 r4 r2 | }
\score { \new Staff { \clef "bass" \partCombine \pcvOne \pcvTwo } \pclay "PCV" }
