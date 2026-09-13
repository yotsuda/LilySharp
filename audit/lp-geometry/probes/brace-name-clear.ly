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
%%    ⚠️⚠️ THE NAME'S FIGURES DEPEND ON THE BACKEND, and the 2026-08-04
%%    "re-measurement" that once retracted the older ones was the unpinned-font
%%    trap. Re-run 2026-09-13 (session 376, scratch/p377/probes), this file
%%    unmodified, through C:\bin\lilypond-2.26.0:
%%
%%        -dbackend=null  InstrumentName    -1.948042 .. 6.417069   (Soprano, 8.365110 wide)
%%        -dbackend=svg   InstrumentName    -1.418820 .. 5.887847   (Soprano, 7.306668 wide)
%%        both            SystemStartBrace   6.8024267716535425 .. 8.175826771653544
%%        clearance       0.385358 (null)  /  0.914580 (svg)
%%
%%    Under svg LilyPond 2.26 drops fonts.serif to the generic "serif" and
%%    fontconfig picks a machine face (ly/paper-defaults-init.ly:169-181); the null
%%    backend keeps "LilyPond Serif". So the OLD "8.365 wide / 0.385 clear" were
%%    LilyPond's numbers and the 7.307 / 0.915 that replaced them were this
%%    machine's fallback face. The brace is Emmentaler and agrees under both. Do
%%    not port either clearance as a constant in any case: read calc-x-offset —
%%    the clearance is `indent - total-left` plus the 0.3 padding plus a
%%    right-padding term that is zero only while the name is narrower than the
%%    indent. It is a placement rule, not a gap.
%%
%%    ⚠️ THE INDENT IS 8.535827 ss — LilyPond's own `(ly:output-def-lookup layout
%%    'indent)`, dumped by instrument-name-x.ly — not the 8.503937 this note used
%%    to derive from 15\mm (that conversion took 1.763889 mm per staff space;
%%    LilyPond's is 1.757299). Lily#'s default indent now reads the same constant
%%    (LayoutEngine.Finishing.cs DefaultIndent), so the old "Lily#'s is 12.0" no
%%    longer holds either; Lily# puts the brace's right edge at indent - 0.3
%%    (MultiStaffLayouter braceX).
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
%%    ✅ THE RESIDUAL THIS NOTE LEFT OPEN IS EXPLAINED (session 376). It read
%%    0.028110 only because of the wrong 8.503937 indent above. Against LilyPond's
%%    own 8.535827 the brace's right edge 8.175827 sits 0.060000 inside
%%    indent - 0.3, and 0.060000 is exactly indent - SystemStartBar left
%%    (8.535827 - 8.475827): instrument-name-x.ly shows the brace is
%%    side-positioned against the SystemStartBar LilyPond adds to every
%%    multi-staff system, not against the indent. Lily# still anchors it on the
%%    indent — unported, HANDOFF §2 E.

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
