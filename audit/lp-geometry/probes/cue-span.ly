\version "2.26.0"
%
% WHAT IS A CUE, AS FAR AS LILYPOND IS CONCERNED — AND WHAT CROSSES ITS BOUNDARY?
%
% WHY THIS EXISTS. Lily# spelled a cue as a per-NOTE annotation (`e4@cue`) and LilyPond has
% no such thing: its cue is a CONTEXT, `\new CueVoice { … }` (ly/engraver-init.ly), whose
% size comes from a CONTEXT PROPERTY `fontSize = #-4` and four context-level overrides. A
% per-note mark can say WHICH notes are cued but not WHERE the region starts and ends, and
% the region boundary is observable — so `lysc ly` could not emit a twin at all and every
% question below had to be answered before a `cue { … }` grammar could be specified.
% Each line of docs/cue-context-design.md that says "MEASURED" points here.
%
% ⚠️ This file opens NO ledger point. It is evidence for a GRAMMAR decision, not a geometry
% measurement — the numbers that will become points (the 13-design head and accidental) are
% in section A and belong to a later commit that ports EngravingDefaults.CueScale.
%
% Output: one line per grob of interest.
%   CUEP <name> <what>
\paper { indent = 0 ragged-right = ##t }

#(define (count name kind)
   (lambda (g) (format #t "CUEP ~a ~a\n" name kind)))
#(define (dumph name)
   (lambda (g)
     (format #t "CUEP ~a head pos=~a fontsize=~a x=~a width=~a\n" name
             (ly:grob-property g 'staff-position)
             (ly:grob-property g 'font-size)
             (ly:grob-relative-coordinate (ly:item-get-column g) (ly:grob-system g) X)
             (cdr (ly:grob-extent g g X)))))
#(define (dumpacc name)
   (lambda (g)
     (format #t "CUEP ~a acc glyph=~a fontsize=~a width=~a\n" name
             (ly:grob-property g 'glyph-name)
             (ly:grob-property g 'font-size)
             (cdr (ly:grob-extent g g X)))))

sweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with {
        \override NoteHead.after-line-breaking   = #(dumph name)
        \override Accidental.after-line-breaking = #(dumpacc name)
        \override Stem.after-line-breaking = #(lambda (g)
          (format #t "CUEP ~a stem dir=~a\n" name (ly:grob-property g 'direction)))
        \override Beam.after-line-breaking    = #(count name "BEAM")
        \override Tie.after-line-breaking     = #(count name "TIE")
        \override Slur.after-line-breaking    = #(count name "SLUR")
        \override Rest.after-line-breaking    = #(count name "REST")
        \override Script.after-line-breaking  = #(count name "SCRIPT")
        \override BarLine.after-line-breaking = #(count name "BAR")
      } { \clef treble $music } #})

% ---- A. THE SIZE, and it is NOT one scale ---------------------------------------------
% The cue head and the cue accidental both report font-size -4, but the head's box does not
% shrink by magstep(-4): a 13-design head is not a 20-design head scaled.
\score { \sweep "A-HIGH" { \time 4/4 g''4 g'' \new CueVoice { g''4 g'' } } }
\score { \sweep "A-CTL"  { \time 4/4 g''4 g'' g''4 g'' } }
\score { \sweep "A-LOW"  { \time 4/4 d'4 d' \new CueVoice { d'4 d' } } }
\score { \sweep "A-ACC"  { \time 4/4 cis''4 dis'' \new CueVoice { fis''4 gis'' } } }

% ---- B. WHAT CROSSES THE BOUNDARY ------------------------------------------------------
\score { \sweep "B-BEAM"  { \time 4/4 c''8 d'' \new CueVoice { e''8 f'' } } }  % 2 beams
\score { \sweep "B-BMCTL" { \time 4/4 c''8 d'' e''8 f'' } }                    % 1 beam
\score { \sweep "B-BMIN"  { \time 4/4 c''4 r4 \new CueVoice { e''8 f'' g'' a'' } } }
\score { \sweep "B-TIE"   { \time 4/4 c''2 ~ \new CueVoice { c''2 } } }        % unterminated
\score { \sweep "B-SLUR"  { \time 4/4 c''2 ( \new CueVoice { d''2 ) } } }      % unterminated
\score { \sweep "B-ACC"   { \time 4/4 cis''2 \new CueVoice { cis''2 } } }      % ONE accidental

% ---- C. WHAT IS LEGAL INSIDE, AND HOW FAR THE REGION REACHES ---------------------------
\score { \sweep "C-BAR"   { \time 4/4 c''2 \new CueVoice { e''2 | f''2 } g''2 } }
\score { \sweep "C-TWO"   { \time 4/4 c''4 \new CueVoice { e''4 } \new CueVoice { f''4 } g''4 } }
\score { \sweep "C-GRACE" { \time 4/4 c''2 \new CueVoice { \grace { d''16 } e''2 } } }
\score { \sweep "C-SCRIPT"{ \time 4/4 c''2 \new CueVoice { e''2 -. } } }
\score { \sweep "C-TUP"   { \time 4/4 c''2 \new CueVoice { \tuplet 3/2 { e''4 f'' g'' } } } }
\score { \sweep "C-REST"  { \time 4/4 c''2 \new CueVoice { r4 e''4 } } }

% ---- D. THE CUE CLEF, which is a property of the REGION and not of any note --------------
% \cueClef draws a SMALL clef before the region and \cueClefUnset a small one after it; the
% cue notes are positioned IN the cue clef, and without the unset the change leaks into the
% rest of the staff.
#(define (dumpclef name)
   (lambda (g)
     (format #t "CUEP ~a CLEF glyph=~a fontsize=~a x=~a\n" name
             (ly:grob-property g 'glyph) (ly:grob-property g 'font-size)
             (ly:grob-relative-coordinate g (ly:grob-system g) X))))
clefsweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with {
        \override Clef.after-line-breaking       = #(dumpclef name)
        \override CueClef.after-line-breaking    = #(dumpclef name)
        \override CueEndClef.after-line-breaking = #(dumpclef name)
        \override NoteHead.after-line-breaking   = #(dumph name)
      } { \clef treble $music } #})

\score { \clefsweep "D-WITH"    { \time 4/4 c''2 \cueClef bass \new CueVoice { e2 } \cueClefUnset c''2 } }
\score { \clefsweep "D-NONE"    { \time 4/4 c''2 \new CueVoice { e''2 } c''2 } }
\score { \clefsweep "D-NOUNSET" { \time 4/4 c''2 \cueClef bass \new CueVoice { e2 } c''2 } }

% ---------------------------------------------------------------------------------------
% WHAT THIS FILE FOUND (2026-08-02, session 72)
%
% A. THE SIZE IS A CONTEXT PROPERTY AND THE GLYPHS ARE PER-DESIGN
%   cue head       font-size -4   width 0.815348908   against full size 1.304200 (ratio 0.625172)
%   cue sharp      font-size -4   width 0.692956577   against full size 1.100000 (ratio 0.629961)
%   ⇒ the ACCIDENTAL's ratio is exactly magstep(-4) = 0.629961 and the HEAD's is NOT, because
%     the 13 design's head is not the 20 design's head scaled. 0.692956577 is the same number
%     the grace accidental already reads, so GlyphMetrics.AtFontSize / MusicFace /
%     AccidentalSkylinePair(kind, 13) are already the right tools.
%   ⚠️ Lily#'s EngravingDefaults.CueScale = 0.66 is 5.6% larger than the head's ratio, not the
%     4.8% its own comment claims against magstep(-4). One number cannot serve both grobs.
%   ⇒ NO PER-GROB TABLE: head and accidental both answer -4. This is where cue differs from
%     grace, whose scm/music-functions.scm general-grace-settings gives NoteHead -3 but
%     Accidental -4.
%
% B. STEM DIRECTION DOES NOT CHANGE, and this is the claim the handoff had recorded backwards
%   ("並行 voice になるので符尾方向と衝突回避が変わる"). A-HIGH's four stems are all -1, the
%   same as A-CTL's; A-LOW's are all +1 including the cue's. An inline \new CueVoice is
%   SEQUENTIAL, not parallel: it is pitch-derived on both sides of the boundary.
%
% C. WHAT THE BOUNDARY STOPS — this is the whole reason a per-note mark cannot work
%   BEAM      does NOT cross:  B-BEAM prints 2 beams where B-BMCTL prints 1
%   TIE       does NOT cross:  "warning: unterminated tie", no Tie grob
%   SLUR      does NOT cross:  "warning: cannot end slur" + "unterminated slur", no Slur grob
%   ACCIDENTAL STATE DOES cross: B-ACC prints ONE accidental for cis''2 + cue cis''2, i.e.
%     the cue inherits the measure's accidental state (Accidental_engraver sits on Staff)
%   ⇒ three of the four are INVISIBLE to a per-note mark and would have to be guessed.
%
% D. WHAT IS LEGAL INSIDE — all of it, with no warning
%   C-BAR     a cue SPANS a bar line (both heads -4, bar line between, next note full size)
%   C-TWO     two adjacent cue blocks both work; they do not need merging
%   C-GRACE   a grace inside a cue reads font-size -7.0 — the context's -4 and the grace's
%             -3 COMPOUND. (Which Emmentaler design -7 selects is NOT measured here.)
%   C-SCRIPT  a script inside a cue is drawn
%   C-TUP     a tuplet inside a cue is drawn, all heads -4
%   C-REST    a rest inside a cue is drawn
%
% E. THE CUE CLEF IS THE REGION'S, AND IT IS DRAWN SMALL
%   D-WITH     \cueClef bass gives a CueClef grob glyph=clefs.F FONT-SIZE -4 before the region,
%              and \cueClefUnset a CueEndClef glyph=clefs.G also at -4 after it. The cue note
%              e2 reads staff-position 1, i.e. it is positioned IN THE CUE CLEF (E3 in bass),
%              and the note after the unset is back to the staff's own clef.
%   D-NOUNSET  WITHOUT the unset the change LEAKS: the following c''2 reads position 13, still
%              in bass. ⇒ a Lily# `cue <clef> { … }` must emit BOTH \cueClef and \cueClefUnset.
%   D-NONE     no cue clef, no CueClef/CueEndClef grob — the argument is genuinely optional.
