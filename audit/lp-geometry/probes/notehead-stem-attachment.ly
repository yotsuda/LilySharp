\version "2.24.0"

% =========================================================================================
% WHERE DOES A STEM MEET ITS HEAD? -- one question, asked of LilyPond directly.
%
% WHY THIS PROBE EXISTS (2026-08-04, session 85). SpacingRules.StemBeginPosition holds TWO
% spellings of one quantity and they disagree at design 20:
%
%   normalised  : BlackHeadStemAttachY 0.34147639283381404 put back through our own bbox
%                 (+-0.545) gives a begin offset of 0.372209268188857
%   the font    : the LILC attachment itself, 0.186200 * 2            = 0.372400000000000
%   difference                                                          0.000190731811143
%
% LilyPond normalises out of the FONT's char dimensions and puts back with the same box
% (note-head.cc:181-189 `att[a] = 2 * (wxwy[a] - v.center ()) / v.length ()`, where
% `b = fm->get_indexed_char_dimensions (k)`; stem.cc:934-963 puts it back), so for LilyPond
% the round trip IS the identity and the answer is simply the attachment point. The two
% spellings can only disagree if OUR box is not the box the constant was normalised out of.
%
% AND THE TWO HEADS BEHAVE DIFFERENTLY, which is what makes this worth measuring rather than
% arguing about. Against our own +-0.545 box:
%
%                 LILC attachment   round-tripped through our box   implied box height
%   black s2      0.186200          0.186104634                     1.090558551
%   half  s1      0.259000          0.259006099                     1.089974333
%
% The half head comes back to six decimal places; the black head misses by 0.0000954. But the
% EXTRACTOR gives both heads the SAME height in every design it emits (11 -> 0.568269,
% 13 -> 0.562220, 14 -> 0.557284, 16 -> 0.552741, 18 -> 0.548597, 23 -> 0.541826,
% 26 -> 0.538889), so one box cannot satisfy both constants. One of the two numbers is not
% from where its comment says it is -- and the comment on the normalised pair says
% "dumped from NoteHead.stem-attachment on LilyPond 2.24.4", while every other measurement in
% this corpus is 2.26.0, whose Emmentaler was REBUILT.
%
% SO: ask 2.26.0. Both heads, on the MIDDLE LINE, where the head position contributes 0 and
% stem-begin-position IS the offset. Reading `stem-attachment` alone would not settle it --
% that is the normalised number, and the question is which box it was normalised out of -- so
% the grob's own Y-extent is dumped beside it.
%
% ---------------------------------------------------------------------------------------
% MEASURED (2026-08-04, session 85). LilyPond 2.26.0.
%
%                stem-attachment          Y-extent      X-extent        stem-begin-position
%   NSA-BLACK    0.341651376146789        +-0.545       0 .. 1.30420    -0.372400
%   NSA-HALF     0.475229357798165        +-0.545       0 .. 1.37740    -0.518000
%   NSA-BLACK-D  (same, sign flipped)     +-0.545       0 .. 1.30420    -0.627600
%   NSA-HALF-D   (same, sign flipped)     +-0.545       0 .. 1.37740    -0.482000
%
% (1) THE BOX WAS NEVER THE SUSPECT. LilyPond's head extent is +-0.545, which is OUR box to
%     every digit it prints. The pair of constants was the whole of the discrepancy: LilyPond
%     2.26.0 says 0.341651376146789 where the code held 0.34147639283381404, and
%     0.475229357798165 where it held 0.4752405486932206. 2 * 0.186200 / 1.090 IS
%     0.341651376146789 -- so with the CURRENT font the round trip closes exactly, as
%     note-head.cc says it must.
%
% (2) => THE OFFSET IS THE ATTACHMENT POINT. -0.372400 = 0.186200 * 2 and -0.518000 =
%     0.259000 * 2, i.e. the LILC attachment itself, with nothing in between.
%
% (3) THE FALSIFIER HELD. At staff position -2 the two heads read -0.6276 and -0.4820, which
%     are -2 + 0.3724 and -2 + 0.518 to fifteen digits. An offset that only matched on the
%     middle line would have been a fit to one register; this is the term.
%
% (4) ⚠️ THE CONSTANTS' OWN COMMENT SAID WHERE THEY CAME FROM -- "dumped on LilyPond 2.24.4" --
%     and 2.26.0 REBUILT Emmentaler. THAT is the class of defect: a number copied out of one
%     release and read against another release's font agrees with itself, sits close to the
%     truth, and passes every check that does not ask the version in use. Ledger point
%     barline.next.down-stems-after-clef had recorded the consequence (5.449e-06) and had even
%     written "appeared with the 2.26.0 font", but could not isolate which metric carried it.
%     ⇒ The fix is not to re-dump the constants. It is to DELETE them and read the font, so
%     that there is no vintage left to go stale the next time the bundled font moves.
% =========================================================================================

#(define (dumphead name)
   (lambda (g)
     (format #t "PROBE ~a HEAD attach=~a yext=~a xext=~a\n" name
             (ly:grob-property g 'stem-attachment)
             (ly:grob-extent g g Y)
             (ly:grob-extent g g X))))

#(define (dumpstem name)
   (lambda (g)
     (format #t "PROBE ~a STEM begin=~a end=~a\n" name
             (ly:grob-property g 'stem-begin-position)
             (ly:grob-property g 'stem-end-position))))

attachsweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with {
        \override NoteHead.after-line-breaking = #(dumphead name)
        \override Stem.after-line-breaking = #(dumpstem name)
      } { \clef treble \autoBeamOff $music } #})

% b' is the middle line: head_position = 0, so stem-begin-position is the offset alone and
% no arithmetic stands between the dump and the number in question.
\score { \attachsweep "NSA-BLACK" { \time 4/4 b'4 b'4 b'4 b'4 } }
\score { \attachsweep "NSA-HALF"  { \time 4/4 b'2 b'2 } }

% The falsifier for "it is just the middle line": the same two heads one space lower, where
% the offset has to be added to a NON-zero head position. If the offset is what this probe
% says it is, these come out as (position + offset) exactly.
\score { \attachsweep "NSA-BLACK-D" { \time 4/4 a'4 a'4 a'4 a'4 } }
\score { \attachsweep "NSA-HALF-D"  { \time 4/4 a'2 a'2 } }
