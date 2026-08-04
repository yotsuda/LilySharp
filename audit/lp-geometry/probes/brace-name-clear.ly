\version "2.26.0"

%% Does LilyPond's instrument name clear the grand-staff brace, and how wide is
%% the brace it draws? Two questions one dump answers, both of them asked
%% because Lily# got them wrong in different ways.
%%
%% 1. CLEARANCE. Lily# centres the name at `Indent / 2` and never looks at the
%%    delimiter, so a name as long as "Soprano" runs into the brace. LilyPond
%%    positions the name by SIDE-POSITION against the delimiter and then
%%    corrects by the indent (scm/output-lib.scm:2108-2142
%%    system-start-text::calc-x-offset), so it structurally cannot overlap.
%%
%%    ⚠️ RE-MEASURED 2026-08-04, and the figures that used to stand here did not
%%    reproduce. This file, unmodified, through C:\bin\lilypond-2.26.0 gives
%%
%%        SystemStartBrace   6.8024267716535425 .. 8.175826771653544
%%        InstrumentName     -1.4188204724409452 .. 5.887847244094488   (Soprano)
%%        clearance          0.914580
%%
%%    The brace agrees with the old note to fifteen digits; the name does not
%%    agree in POSITION OR WIDTH (old 8.365 wide, measured 7.307), so the old
%%    "0.385 clear" is not this book's number and was most likely carried over
%%    from an earlier draft of the probe. Do not port 0.385 as a constant in any
%%    case: read calc-x-offset — the clearance is `indent - total-left` plus the
%%    0.3 padding plus a right-padding term that is zero only while the name is
%%    narrower than the indent. It is a placement rule, not a gap.
%%
%%    ⚠️ AND THE TWO ENGINES' INDENTS DIFFER, so no figure here may be compared
%%    with a Lily# figure until a twin fixes it: LilyPond's default indent is
%%    15\mm = 8.503937 ss, Lily#'s is 12.0 ss. Measured the same day, Lily#'s
%%    brace right edge is 11.70 = its indent - 0.3.
%%
%% 2. THE BRACE'S OWN WIDTH, which is the cross-check on the ladder port: the
%%    brace picked for this four-staff span is 8.1758 - 6.8024 = 1.3734 wide,
%%    and the glyph BraceLadder.NearestIndex picks for the same span carries the
%%    same width in brace-ladder.ly's dump. That is the only confirmation the
%%    drawing side's "one em is four staff spaces" has (see
%%    SharedRenderer.DrawSystemStartBrace, which says so). Still 1.373400 on the
%%    re-measurement, so that confirmation stands.
%%
%% 3. AND ONE THING THIS DUMP RETIRES RATHER THAN OPENS. The brace's X was
%%    written up as un-ported because `staff_brace` centres the stencil and then
%%    translates -0.2 (lily/system-start-delimiter.cc:150-160) while Lily#
%%    right-anchors at BraceX. Those two lines CANCEL: X-offset is
%%    ly:side-position-interface::x-aligned-side, and aligned_side positions the
%%    grob by its own extent (lily/side-position-interface.cc:189 aligned_side,
%%    "taking into account my own dimensions and padding"), so centring the
%%    stencil and shifting it inside the grob moves the extent with it and the
%%    INK still lands at (support edge - padding). Lily# already puts the right
%%    edge at indent - 0.3. This is the flag's offset/extent pair again: reading
%%    one half of a self-cancelling pair and calling it a defect.
%%    ⚠️ WHAT IS NOT YET EXPLAINED, and is the only live question left here:
%%    LilyPond's brace right edge is 8.175827 while its indent - 0.3 is
%%    8.203937, a residual of 0.028110. Whatever that is, it is not the -0.2.

#(define (dump-x name)
   (lambda (grob)
     (let ((x (ly:grob-extent grob (ly:grob-system grob) X)))
       (format (current-error-port) "~a X = ~a .. ~a\n" name (car x) (cdr x)))
     (ly:grob-set-property! grob 'after-line-breaking #f)
     '()))

\score {
  \new GrandStaff <<
    \new Staff \with { instrumentName = "Soprano" } { c'1 }
    \new Staff \with { instrumentName = "Alto" }    { c'1 }
    \new Staff \with { instrumentName = "Tenor" }   { c'1 }
    \new Staff \with { instrumentName = "Bass" }    { \clef bass c1 }
  >>
  \layout {
    \context {
      \Score
      \override InstrumentName.after-line-breaking = #(dump-x "InstrumentName")
      \override SystemStartBrace.after-line-breaking = #(dump-x "SystemStartBrace")
    }
  }
}
