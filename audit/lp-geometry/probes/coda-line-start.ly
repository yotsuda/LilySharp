\version "2.26.0"
%
% WHERE DOES A CODA MARK SIT WHEN THE LINE OPENS WITH A REPEAT BAR LINE?
%
% Lily# draws it at the system's LEFT EDGE (x 0.30), left of the clef, while the |: stands at
% 6.44 — the owner expects the bar line. CodaMark declares
%   (break-align-symbols . (staff-bar key-signature clef))   scm/define-grobs.scm:1006
% i.e. the STAFF BAR first, where SectionLabel declares (left-edge staff-bar) at :3047. So the
% two should not be placed alike, and Lily# places them alike.
%
% ⚠️ CodaMark also declares (break-visibility . begin-of-line-invisible) at :1007, so a mark
% whose moment IS a line break may not appear on the new line at all — that is part of what
% this asks, which is why CB2/CB3 break exactly at the mark and CB1 does not.
%
% ⚠️ THE OVERRIDE MUST BE IN THE **SCORE** CONTEXT. The first draft of this file put it on
% \new Staff and dumped NOTHING — CodaMark is made by the Score-level engraver, so a Staff
% override never reaches it, and the empty output looked exactly like "LilyPond draws no coda
% here" (HANDOFF §5.3).
%
% PROBECB <name> <what> x=<x in system> ext=<X-extent> breakdir=<-1 end | 0 mid | 1 begin>
\paper { indent = 0 ragged-right = ##f line-width = 60 }

#(define (dump name what)
   (lambda (g)
     (format #t "\nPROBECB ~a ~a x=~a ext=~a breakdir=~a\n" name what
             (ly:grob-relative-coordinate g (ly:grob-system g) X)
             (ly:grob-property g 'X-extent)
             (ly:item-break-dir g))))

% CB1 — MID-LINE: the coda mark immediately before a repeat bar line, no break involved.
\score { { c'1 | \codaMark \default \repeat volta 2 { c'1 | c'1 | } }
  \layout { \context { \Score
    \override CodaMark.after-line-breaking = #(dump "CB1" "CODA")
    \override BarLine.after-line-breaking = #(dump "CB1" "BAR")
    \override Clef.after-line-breaking = #(dump "CB1" "CLEF") } } }

% CB2 — THE OWNER'S SHAPE: a line break at the mark, so the new line opens clef, |: and the
%       coda mark belongs to that moment.
\score { { c'1 | c'1 | \break \codaMark \default \repeat volta 2 { c'1 | c'1 | } }
  \layout { \context { \Score
    \override CodaMark.after-line-breaking = #(dump "CB2" "CODA")
    \override BarLine.after-line-breaking = #(dump "CB2" "BAR")
    \override Clef.after-line-breaking = #(dump "CB2" "CLEF") } } }

% CB3 — the same break with a KEY signature too, the other symbol CodaMark's list names.
\score { { \key g \major c'1 | c'1 | \break \codaMark \default \repeat volta 2 { c'1 | c'1 | } }
  \layout { \context { \Score
    \override CodaMark.after-line-breaking = #(dump "CB3" "CODA")
    \override BarLine.after-line-breaking = #(dump "CB3" "BAR")
    \override Clef.after-line-breaking = #(dump "CB3" "CLEF")
    \override KeySignature.after-line-breaking = #(dump "CB3" "KEY") } } }

% CB4 — CONTROL: a SECTION LABEL at the same place. It declares (left-edge staff-bar), so if
%       the two really differ, this one goes to the edge and the coda does not.
\score { { c'1 | c'1 | \break \sectionLabel "E" \repeat volta 2 { c'1 | c'1 | } }
  \layout { \context { \Score
    \override SectionLabel.after-line-breaking = #(dump "CB4" "LABEL")
    \override BarLine.after-line-breaking = #(dump "CB4" "BAR")
    \override Clef.after-line-breaking = #(dump "CB4" "CLEF") } } }

% -----------------------------------------------------------------------------------------
% WHAT THIS FILE FOUND (2026-08-18, session 206)
%
%   score  what                                       x
%   CB1    |: (ink 1.84 wide)                         14.555113320607614
%          CodaMark (ext -1.024736 . +1.024736)       15.100113320607614   breakdir 0
%   CB2    line 1's closing bar line                  33.953307086614170   breakdir -1
%          CodaMark                                   34.048307086614166   breakdir -1
%          line 2's |:                                 4.064999999999999   breakdir +1
%          CodaMark on line 2                         -- NONE --
%   CB3    same as CB2 with a key signature           34.048307086614170   breakdir -1
%   CB4    SectionLabel on line 2 (control)            0.0                 breakdir +1
%
% ⇒ MID-LINE the coda sits ON the bar line: 15.100113 against the bar's 14.555113, i.e.
%   inside its 1.84 of ink, not at any measure edge. That is (break-align-symbols .
%   (staff-bar key-signature clef)) choosing the staff bar.
% ⇒ AT A BREAK LilyPond does not put it on the new line AT ALL. CodaMark declares
%   (break-visibility . begin-of-line-invisible), so the copy that prints is the END-OF-LINE
%   one, and the new line opens with just clef / key / |:.
% ⇒ THE CONTROL IS WHAT MAKES THAT A MEASUREMENT: SectionLabel, declaring
%   (left-edge staff-bar), DOES appear on the new line, at x 0.0. So "no CodaMark on line 2"
%   is the engine's answer and not a probe that failed to look.
%
% ⚠️ Lily# does NOT port the visibility: the owner chose (2026-08-18) to keep the sign on the
% new line and align it to the bar line drawn there. There is therefore no LilyPond number for
% that placement — only for the RULE that picks the grob to align to, which is what
% MusicMarkEngraver.CalculateXPosition now follows. If the visibility is ever ported, these
% readings are what it must reproduce.