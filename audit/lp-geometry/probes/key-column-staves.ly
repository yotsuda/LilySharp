\version "2.26.0"
%
% DOES A MID-LINE KEY CHANGE COLUMN GET WIDER WITH MORE STAVES?
%
% THE DEFECT THIS WAS OPENED FOR (2026-08-18, session 206). An owner's two-staff book showed
% a blank the width of a signature between the key change opening a section and that section's
% first note, and read it as space reserved for a time signature that was never drawn. There
% is no meter change in that book at all (`lysc layout` reports none). What was reserved was
% the KEY signature, once per staff, SUMMED:
%
%   3-sharp change, bar-line ink right -> first note head, Lily# before the fix
%     1 staff  1.64      2 staves  4.94      3 staves  8.24        (+3.300030 each)
%   1-sharp change
%     2 staves 3.77      3 staves  4.87                            (+1.100010 each)
%
% i.e. exactly one A-major / G-major signature per extra staff. A column's item list is
% aggregated across staves (MeasureLayouter.BuildTimingToItemsMap) and the width walks added
% every member as though they were grobs standing side by side, which is true of a clef and a
% key and a meter arriving together on ONE staff and false of the same key arriving on N.
%
% ⚠️ THE QUESTION IS SUM-OR-MAX, and it needs the staff count varied while everything else is
% held still. ragged-right so the numbers are natural widths and comparable between books.
%
% PROBEKS <name> <what> x=<x in system> ext=<X-extent>
\paper { indent = 0 ragged-right = ##t line-width = 120 }

#(define (dump name what)
   (lambda (g)
     (format #t "\nPROBEKS ~a ~a x=~a ext=~a\n" name what
             (ly:grob-relative-coordinate g (ly:grob-system g) X)
             (ly:grob-property g 'X-extent))))

mus = { c'1 | \key a \major c'1 | }

% KS1 / KS2 / KS3 -- the same music on one, two and three staves. Only the TOP staff dumps
% its bar line and note head; the lower staves dump their KeySignature only, which is what
% shows that every staff really does print one (a probe that saw no signature on staff 2
% would report "no widening" for the wrong reason -- HANDOFF §5.3).
\score { \new PianoStaff <<
  \new Staff \with {
    \override KeySignature.after-line-breaking = #(dump "KS1" "KEY")
    \override BarLine.after-line-breaking = #(dump "KS1" "BAR")
    \override NoteHead.after-line-breaking = #(dump "KS1" "HEAD")
  } \mus
>> \layout {} }

\score { \new PianoStaff <<
  \new Staff \with {
    \override KeySignature.after-line-breaking = #(dump "KS2" "KEY")
    \override BarLine.after-line-breaking = #(dump "KS2" "BAR")
    \override NoteHead.after-line-breaking = #(dump "KS2" "HEAD")
  } \mus
  \new Staff \with {
    \override KeySignature.after-line-breaking = #(dump "KS2b" "KEY")
  } \mus
>> \layout {} }

\score { \new PianoStaff <<
  \new Staff \with {
    \override KeySignature.after-line-breaking = #(dump "KS3" "KEY")
    \override BarLine.after-line-breaking = #(dump "KS3" "BAR")
    \override NoteHead.after-line-breaking = #(dump "KS3" "HEAD")
  } \mus
  \new Staff \with {
    \override KeySignature.after-line-breaking = #(dump "KS3b" "KEY")
  } \mus
  \new Staff \with {
    \override KeySignature.after-line-breaking = #(dump "KS3c" "KEY")
  } \mus
>> \layout {} }

% -----------------------------------------------------------------------------------------
% WHAT THIS FILE FOUND (2026-08-18, session 206)
%
%   score   BAR                 KEY (ext 0 . 3.3)   next HEAD
%   KS1     14.645044999134612  15.835044999134611  22.635044999134614
%   KS2     14.645044999134612  15.835044999134611  22.635044999134614
%   KS3     14.645044999134612  15.835044999134611  22.63504499913461
%
%   KS2b KEY 15.835044999134611      KS3b / KS3c KEY 15.835044999134611
%
% ⇒ IDENTICAL TO TWELVE DIGITS. The column does NOT widen with the staff count: every staff
%   prints its own signature at the SAME x, and the column is one signature wide. MAX, not
%   sum -- and max rather than "the first staff's" is what covers staves whose signatures
%   differ from one another, which this file does not exercise and the Lily# side takes on
%   structure (SpacingRules.WidestChangeOfKind).
% ⇒ The lower staves' dumps are here to prove the instrument: KS2b and KS3b/KS3c report a
%   KeySignature at that same x, so "no widening" is not "no signature was engraved".
% ⇒ bar ink right 14.835044999134612 -> KEY 15.835044999134611 is 1.000000, BarLine's own
%   (key-signature . (extra-space . 1.0)) at scm/define-grobs.scm:297 -- the same entry
%   courtesy.meter.barline-to-cancellation measures at a line end.
