\version "2.26.0"
%% LP FIDELITY PROBE — a MID-PIECE meter change on a staff that engraves no meter.
%%
%% Produces the numbers in ../lp-geometry.json under "mid-piece.tab-numbers.*". Run it with
%% ../Measure-LilyPondProbe.ps1 -Probe tab-numbers-meter.ly.
%%
%% WHY THIS EXISTS. Session 198 closed the LINE-START half of this question:
%% SpacingRules.AnyStaffEngravesTime now asks whether any staff draws a meter before the
%% prefix books a meter column, so an all-`tab … as numbers` score no longer reserves one at
%% the head of a system. It left the MID-PIECE half open, and named the three pure functions
%% it lives in — SpacingRules.BarlineToFirstColumnSpring / BoundaryChangePrefix (a change at
%% a bar line) and SpacingRules.MidMeasureChangeGaps (a change inside a bar). Those walk an
%% IReadOnlyList<MusicItem> and know nothing about staves, so they price a
%% TimeSignatureChangeItem whatever the score is made of.
%%
%% WHAT LILYPOND DOES, read from the source before measuring. A bare TabStaff keeps its
%% Time_signature_engraver and BLANKS the grob — ly/engraver-init.ly:1219-1220
%% \override TimeSignature.stencil = ##f — so a TimeSignature ITEM exists in the non-musical
%% column with an EMPTY X-extent. Two walks then skip it, and between them they are the whole
%% of the column's cost:
%%   lily/break-alignment-interface.cc:144-156 calc_positioning_done — the alignment walk
%%     steps over every element whose extent is_empty (), so a blanked grob takes no
%%     space-alist offset and widens nothing.
%%   lily/spacing-interface.cc:217-220 extremal_break_aligned_grob — `if (ext.is_empty ())
%%     continue;`, so a blanked grob never becomes the `last_grob` whose space-alist prices
%%     the following note either.
%% ⇒ PREDICTED BEFORE THE DUMP: the column is not merely zero-WIDE, it is ABSENT — a bare
%% TabStaff's mid-piece meter costs nothing at all, not even the
%% (first-note . (semi-shrink-space . 2.0)) distance a drawn one offers.
%%
%% ============ THE PAIRS ============
%%
%% The twins are IDENTITIES on LilyPond's side, which is what makes any Lily# difference the
%% size of a Lily# defect by construction (HANDOFF 5.0: "the strongest pair is one LilyPond
%% reads as an identity"). 2/4 and 16/32 are the SAME measure length, so the bar grid, the
%% note count and every duration are identical; the two differ only in a glyph nobody draws.
%%
%%   TN  bare TabStaff, mid-piece \time 2/4        the narrow meter
%%   TW  bare TabStaff, mid-piece \time 16/32      the wide one   -> must equal TN
%%   TL  bare TabStaff, the same grid with NO \time at all (\set Timing.measureLength = #1/2)
%%                                                 -> must equal TN as well, and THAT is what
%%                                                 says the column is absent rather than 0 wide
%%   MN  bare TabStaff, the change INSIDE a bar (MidMeasureChangeGaps' half), 2/4
%%   MW  the same, 16/32                           -> must equal MN
%%   FN  \tabFullNotation, mid-piece \time 2/4     THE CONTROL / POISON
%%   FW  \tabFullNotation, mid-piece \time 16/32   -> must DIFFER from FN
%%
%% ⚠️ FN/FW ARE NOT DECORATION. A sweep that reports "no difference" says nothing until the
%% same sweep is shown to report one where a difference exists (HANDOFF 5.3). \tabFullNotation
%% reverts exactly the one \override this probe is about (ly/property-init.ly:825-826) and
%% changes nothing else, so FN != FW is the narrowest possible demonstration that the
%% measurement reaches the quantity.
%%
%% ============ MEASURED 2026-08-18 on LilyPond 2.26.0 ============
%%
%% Byte comparison of the rendered SVG (HANDOFF 5.0: "acceptance is settled by a control and
%% bytes, not by whether the other side stayed quiet"):
%%
%%   TN vs TW   IDENTICAL   (sha256 29AD0B45…, 8394 bytes both)
%%   TN vs TL   IDENTICAL   (the same 29AD0B45…) -> the blanked meter costs nothing, as
%%                          predicted: not one byte separates "\time 2/4" from the same bar
%%                          grid reached with no \time command at all
%%   MN vs MW   IDENTICAL   (7485 bytes both)
%%   FN vs FW   DIFFER      (11810 vs 12674 bytes) -> the sweep reaches
%%
%% The PROBE lines below give the same answer as figures, which is what the ledger records:
%% the bar line at the change and the first fret digit after it. BAR ink is (0 . 0.19), so the
%% ink right edge is x + 0.19.
%%
%%   TN  bar 0 (plain, opens a 4/4 bar)      ink right 16.899479095239367
%%       first fret after it                           17.844992533229295 -> 0.945513437989928
%%   TN  bar 1 (THE CHANGE, opens a 2/4 bar) ink right 28.944471628468668
%%       first fret after it                           29.889985066458596 -> 0.945513437989928
%%   TN  bar 2 (plain, opens a 2/4 bar)      ink right 35.640376998074410
%%       first fret after it                           36.561883860142920 -> 0.945513437989928
%%
%% ⇒ THE SAME NUMBER TO FIFTEEN DIGITS, ALL THREE. A bar that carries a mid-piece meter change
%%   and a bar that carries nothing put the first fret at exactly the same distance. That is
%%   the ledger point the identity twin CANNOT make: an identity is blind to a symmetric error
%%   by definition (HANDOFF 5.0), and Lily# booking a CONSTANT column for every meter would
%%   leave TN/TW exact while still being wrong. Two points are needed, and they fail in
%%   different ways: the twin catches a width that depends on the digits, the plain-bar
%%   comparison catches a column that exists at all.
%%
%% ⚠️ EVERY BAR OF TN/TW OPENS ON c', AND THAT IS NOT COSMETIC. Written the obvious way — the
%%   last bar opening on g' — bar 2 read 0.921506862 instead, and the first explanation
%%   written down ("it is the last bar of a ragged-right line, where the slack lands") was
%%   REFUTED by adding a fifth bar: the 0.921506862 stayed on bar 2 and the new last bar read
%%   0.945513438. Changing only the opening pitch g' -> c' put all three on 0.945513438. The
%%   quantity is Staff_spacing's own next-note correction (lily/staff-spacing.cc:95-110
%%   next_notes_correction, which prices each right note column against the bar line's Y
%%   extent from bar_y_positions), so it moves with the STRING the fret digit sits on. One
%%   variable per pair (HANDOFF 5.0): the meter is the variable here, so the string is held.
%%
%% ragged-right, indent 0: every spring at force 0, so what is read is the ideal.

\header { tagline = ##f }

%% Same dumper as staffless-system.ly: the grob's own anchor and ink, then the X of the paper
%% column it hangs from. `ext=(0 . 0)` on the COL# line is a placeholder, not a measurement.
#(define ((gd tag name) g)
   (let* ((sys (ly:grob-system g))
          (col (ly:grob-parent g X)))
     (format #t "\nPROBE ~a ~a x=~a ext=~a\n" tag name
             (ly:grob-relative-coordinate g sys X)
             (ly:grob-extent g g X))
     (format #t "\nPROBE ~a ~aCOL#~a x=~a ext=(0 . 0)\n" tag name
             (ly:grob-property col 'rank)
             (ly:grob-relative-coordinate col sys X))))

lay =
#(define-scheme-function (tag) (string?)
   #{
     \layout {
       ragged-right = ##t
       line-width = 500\mm
       indent = 0
       \context {
         \Score
         \override BarLine.after-line-breaking       = #(gd tag "BAR")
         \override Clef.after-line-breaking          = #(gd tag "CLEF")
         \override TimeSignature.after-line-breaking = #(gd tag "TIME")
         \override TabNoteHead.after-line-breaking   = #(gd tag "FRET")
       }
     }
   #})

%% ⚠️ The TimeSignature override above fires on the BLANKED grob too — a stencil of ##f does
%% not stop after-line-breaking. So a TIME line in TN's dump is not a contradiction: it is the
%% direct evidence that the grob EXISTS, which is the whole reason the two skipping walks
%% cited in the header are where the answer lives rather than "no grob is made". Its dumped
%% ink confirms it from the other side: `ext=(+inf.0 . -inf.0)`, LilyPond's EMPTY interval.
%%
%% ⚠️ FN AND FW DUMP NO FRET LINES, AND THAT IS NOT A HOLE IN THE PROBE. LilyPond ERASES the
%% callback under \tabFullNotation: scm/scheme-engravers.scm:2196-2200 Tab_tie_follow_engraver
%% reads the tabFullNotation context property and, when it is set, does
%% `(ly:grob-set-property! grob 'after-line-breaking '())` on every TabNoteHead — its own
%% comment says it is to avoid printing transparent and parenthesized. Verified in isolation
%% (two scores, one \tabFullNotation, same override: only the bare one dumps). The control
%% therefore reads its BAR lines, which are unaffected, and that is enough: FN's bar after the
%% change sits at 41.342172 and FW's at 42.571331, so the sweep demonstrably reaches.

%% TN — the narrow half. Four bars: two of 4/4, then the change, then two of 2/4.
\score {
  \new TabStaff { \time 4/4 c'4 e' g' e' | c'4 e' g' e' | \time 2/4 c'4 e' | c'4 e' | }
  \lay "TN"
}

%% TW — the identity twin. 16/32 is 1/2, exactly as 2/4 is.
\score {
  \new TabStaff { \time 4/4 c'4 e' g' e' | c'4 e' g' e' | \time 16/32 c'4 e' | c'4 e' | }
  \lay "TW"
}

%% TL — the same bar grid with NO meter command. measureLength is a Timing property, not a
%%   TimeSignature grob, so nothing is engraved and nothing is blanked. TL == TN is the
%%   statement that the blanked column is ABSENT; TL != TN would have meant it is present at
%%   zero width and still charging its space-alist distance.
\score {
  \new TabStaff { \time 4/4 c'4 e' g' e' | c'4 e' g' e' |
                  \set Timing.measureLength = #1/2
                  c'4 e' | c'4 e' | }
  \lay "TL"
}

%% MN / MW — the OTHER half: the change lands INSIDE a bar, which is
%%   SpacingRules.MidMeasureChangeGaps rather than the barline prefix. Bar 2 runs long by
%%   construction (LilyPond warns about the bar check and engraves it anyway); the pair is
%%   still an identity because both halves run long by the same amount.
\score {
  \new TabStaff { \time 4/4 c'4 e' g' e' | c'4 e' \time 2/4 g'4 e' | c'4 e' | }
  \lay "MN"
}
\score {
  \new TabStaff { \time 4/4 c'4 e' g' e' | c'4 e' \time 16/32 g'4 e' | c'4 e' | }
  \lay "MW"
}

%% FN / FW — THE CONTROL. \tabFullNotation reverts TimeSignature.stencil (and the stem, beam,
%%   flag, dot, rest and tuplet blanks with it — none of which this music has, so the meter is
%%   the only difference against TN/TW). This is Lily#'s DEFAULT `tab`; TN/TW/MN/MW are
%%   `tab … as numbers`.
\score {
  \new TabStaff { \tabFullNotation \time 4/4 c'4 e' g' e' | c'4 e' g' e' | \time 2/4 c'4 e' | c'4 e' | }
  \lay "FN"
}
\score {
  \new TabStaff { \tabFullNotation \time 4/4 c'4 e' g' e' | c'4 e' g' e' | \time 16/32 c'4 e' | c'4 e' | }
  \lay "FW"
}
