\version "2.26.0"
%
% WHAT STANDS AT THE END OF A LINE WHEN THE NEXT ONE CHANGES METER?
%
% THE DEFECT THIS WAS OPENED FOR (2026-08-02, session 75). Lily# printed the courtesy KEY at
% a line end but never the courtesy METER, so a book that goes 2/4 -> 4/4 across a break lost
% the C at the end of the previous line. LilyPond prints it, and the rule is not "key changes
% are special":
%   scm/define-grobs.scm:3922-3953 — TimeSignature's own break-visibility is `all-visible`,
%     so a signature prints at the END of a line as well as at the start of the next.
%   lily/time-signature-engraver.cc:114-118 Time_signature_engraver::process_music — the
%     grob default is overridden with `initialTimeSignatureVisibility` for the FIRST
%     signature only (guarded by `scm_is_null (last_spec_)`).
%   ly/engraver-init.ly:867 — that property is `end-of-line-invisible`.
% ⇒ the INITIAL meter never shows at a line end; every CHANGED one does. Measured three ways:
%   both change → cancellation, new key, meter;  meter only → meter alone;  neither → bare.
%
% ⚠️ THE BARE CASE IS PART OF THE READING AND HAS NO POINT HERE, because there is nothing to
% measure when nothing is drawn. It is held as an assertion instead (Lily# tests) — a corpus
% that only measures what IS drawn cannot see a courtesy that should NOT have been.
%
% Output, one line per grob:
%   PROBECM <name> <grob> x=<x in the system> ext=<X-extent> breakdir=<-1 end | 1 begin>
\paper { indent = 0 ragged-right = ##f line-width = 60 }

#(define (dump name what)
   (lambda (g)
     (format #t "\nPROBECM ~a ~a x=~a ext=~a breakdir=~a\n" name what
             (ly:grob-relative-coordinate g (ly:grob-system g) X)
             (ly:grob-property g 'X-extent)
             (ly:item-break-dir g))))

sweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with {
        \override TimeSignature.after-line-breaking = #(dump name "TIME")
        \override KeySignature.after-line-breaking = #(dump name "KEY")
        \override KeyCancellation.after-line-breaking = #(dump name "KEYCANCEL")
        \override BarLine.after-line-breaking = #(dump name "BAR")
        %% ADDED 2026-08-18 (session 206): the staff's own extent, so these three scores can
        %% also answer "and how much line is left AFTER the group?" — the question the PROBELE
        %% section at the foot of this file opened. Additive: every PROBECM line recorded
        %% above is unchanged, and was re-run to confirm that before the ledger read it.
        \override StaffSymbol.after-line-breaking =
          #(lambda (g)
             (format #t "\nPROBECM ~a STAFF ext=~a\n" name
                     (ly:grob-extent g (ly:grob-system g) X)))
      } { $music } #})

% A. THE METER ALONE. Only the meter changes at the break, so the line end carries the C and
%    nothing else — the gap is bar-line ink right edge → the meter, with no key in between.
\score { \sweep "CMT" { \clef bass \key ees \major \time 2/4 r2 | \break \time 4/4 d2 e | }
         \layout {} }

% B. BOTH CHANGE, which is the shape a real book has (scratch/ベースタブLy/repro.lys, section D
%    ending 2/4 and section B3 opening 4/4 in a new key). The line end carries cancellation,
%    new key, THEN the meter, and the first of those is what this book measures off the bar.
\score { \sweep "CMK" { \clef bass \key ees \major \time 2/4 r2 | \break \key a \major \time 4/4 d2 e | }
         \layout {} }

% C. THE SAME GAP WITH BOTH GLYPHS CHANGED — a DOUBLE bar line at the break and a NUMERAL
%    meter (3/4) rather than the C that \time 4/4 prints. CMT measured 0.750000 on one book
%    only, and a constant measured once is a constant whose texture-dependence is untested.
%    ⚠️ THE PREDICTION IS THAT IT DOES NOT MOVE, and it is structural rather than empirical:
%    lily/break-alignment-interface.cc:180-210 takes the space-alist off the LEFT grob and
%    keys it by the RIGHT grob's break-align-symbol, and :241-243 places the next group at
%    `extents[idx][RIGHT] + distance - extents[next_idx][LEFT]` -- so the INK-TO-INK gap is
%    exactly `distance` and BOTH extents cancel. BarLine's entry for time-signature is
%    (extra-space . 0.75) at scm/define-grobs.scm:293, and a bar line's space-alist does not
%    depend on its glyph. If this book prints anything but 0.750000, that reading is wrong.
\score { \sweep "CMT3" { \clef bass \key ees \major \time 2/4 r2 \bar "||" \break
                         \time 3/4 d,2. | }
         \layout {} }

% ---------------------------------------------------------------------------------------
% WHAT THIS FILE FOUND (2026-08-02, session 75)
%
%   CMT  BAR 31.003307 ext (0 . 0.19) → ink right 31.193307
%        TIME 31.943307                                        gap 0.750000
%   CMK  BAR 23.353507 ext (0 . 0.19) → ink right 23.543507
%        KEYCANCEL 24.543507                                   gap 1.000000
%        KEY 27.493307 (cancellation ink ends 26.993307)       gap 0.500000
%        TIME 31.943307 (key ink ends 30.793307)               gap 1.150000
%
% ⇒ THE TWO GAPS OFF THE BAR LINE ARE NOT THE SAME NUMBER (0.75 against 1.00). LilyPond has no
%   single "space after the bar line": each break-aligned grob brings its own space-alist
%   entry. Lily# spelled one constant (0.8) for both, which is why CMK opens non-zero.
% ⇒ 1.150000 is ALSO what the line-START prefix puts between key and meter (7.603400 →
%   8.753400 on the same book), which is what says it is break-align spacing and not
%   something the courtesy invented.

% =========================================================================================
% AND WHAT COMES *AFTER* THE COURTESY? (2026-08-18, session 206)
%
% THE DEFECT THIS WAS OPENED FOR. Everything above measures the gaps INSIDE the end-of-line
% group — bar → cancellation → key → meter. Nothing had ever measured the gap from the
% group's LAST member to the end of the line, so Lily# ended the staff line at the courtesy
% meter's advance edge: 0.07 ss of white on the owner's book (scratch/ベースタブLy/time-break.lys),
% which reads as the staff running into the signature.
%
% A break-align group has one more member than the grobs in it: `right-edge`. Every grob that
% can stand last declares its gap to it, and they are not all the same number —
%   scm/define-grobs.scm:3951  TimeSignature   (right-edge . (extra-space . 0.5))
%   scm/define-grobs.scm:1995  KeySignature    (right-edge . (extra-space . 0.5))
%   scm/define-grobs.scm:1946  KeyCancellation (right-edge . (extra-space . 0.5))
%   scm/define-grobs.scm:302   BarLine         (right-edge . (extra-space . 0.0))
% — which is why a line ending in a bar line puts its ink flush on the edge (score NONE) and
% one ending in a courtesy stops 0.5 short of it.
%
% ⚠️ THE STAFF SYMBOL IS NOT THE LINE EDGE. StaffSymbol's X-extent is (0.05 . W-0.05) — inset
% by half the 0.1 line thickness at BOTH ends — so the margin read against the drawn staff
% line is 0.450000 and against the line edge is 0.500000. The alist entry is the second one.
% (Lily# draws its staff line to the edge, without the inset. That ±0.05 is a real difference
% and is NOT this one; it is noted here so the next reader does not close it as this.)
%
% Output: PROBELE <name> <what> x=<x in system> ext=<X-extent> breakdir=<-1 end | 1 begin>

#(define (dumple name what)
   (lambda (g)
     (format #t "\nPROBELE ~a ~a x=~a ext=~a breakdir=~a\n" name what
             (ly:grob-relative-coordinate g (ly:grob-system g) X)
             (ly:grob-property g 'X-extent)
             (ly:item-break-dir g))))

#(define (dumplespan name what)
   (lambda (g)
     (format #t "\nPROBELE ~a ~a ext=~a\n" name what
             (ly:grob-extent g (ly:grob-system g) X))))

edgesweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with {
        \override TimeSignature.after-line-breaking = #(dumple name "TIME")
        \override BarLine.after-line-breaking = #(dumple name "BAR")
        %% ⚠️ THESE TWO WERE MISSING IN THE FIRST DRAFT, and score KEYONLY read "LilyPond
        %% prints no courtesy key here" off an instrument that was not looking for one —
        %% a zero wearing the face of a measurement (HANDOFF §5.3). The staff line's own
        %% position had not moved, which is what made the empty reading plausible.
        \override KeySignature.after-line-breaking = #(dumple name "KEY")
        \override KeyCancellation.after-line-breaking = #(dumple name "KEYCANCEL")
        \override StaffSymbol.after-line-breaking = #(dumplespan name "STAFF")
      } { $music } #})

% NUM  — the owner's shape: 4/4 running, \break, 3/4 arrives. Courtesy meter alone.
\book { \paper { indent = 0 ragged-right = ##f line-width = 60 }
  \score { \edgesweep "NUM" { \time 4/4 c'1 | c'1 | c'1 | \break \time 3/4 c'2. | c'2. | }
           \layout {} } }

% COM  — the same break with a C arriving, so the courtesy glyph inks 1.700000 instead of
%        1.604735. A rule that is a PADDING does not move; one that is a width does.
\book { \paper { indent = 0 ragged-right = ##f line-width = 60 }
  \score { \edgesweep "COM" { \time 3/4 c'2. | c'2. | c'2. | \break \time 4/4 c'1 | c'1 | }
           \layout {} } }

% W12  — 12/8, a two-digit numerator: a third ink width (2.731465) against the same rule.
\book { \paper { indent = 0 ragged-right = ##f line-width = 60 }
  \score { \edgesweep "W12" { \time 4/4 c'1 | c'1 | c'1 | \break \time 12/8 c'1. | c'1. | }
           \layout {} } }

% NONE — the control: no change at the break, so the line ends in a BAR LINE and its 0.0
%        entry is what puts that ink ON the edge. Without this the 0.5 has nothing to be 0.5
%        *against*, and "the line always stops short of its edge" would fit the data equally.
\book { \paper { indent = 0 ragged-right = ##f line-width = 60 }
  \score { \edgesweep "NONE" { \time 4/4 c'1 | c'1 | c'1 | \break c'1 | c'1 | }
           \layout {} } }

% W40 / W90 — the same shape at two other line widths, and RAG with nothing stretched at all.
%        A padding the justifier obeys and a coincidence of one line width look identical
%        until the width moves; ragged is the case where no spring is stretched, so a number
%        that survives it is the group's own and not justification's.
\book { \paper { indent = 0 ragged-right = ##f line-width = 40 }
  \score { \edgesweep "W40" { \time 4/4 c'1 | c'1 | \break \time 3/4 c'2. | c'2. | }
           \layout {} } }
\book { \paper { indent = 0 ragged-right = ##f line-width = 90 }
  \score { \edgesweep "W90" { \time 4/4 c'1 | c'1 | c'1 | c'1 | \break \time 3/4 c'2. | c'2. | }
           \layout {} } }
\book { \paper { indent = 0 ragged-right = ##t line-width = 60 }
  \score { \edgesweep "RAG" { \time 4/4 c'1 | c'1 | \break \time 3/4 c'2. | c'2. | }
           \layout {} } }

% KEYONLY — a courtesy KEY and no meter, so the KEY is last and pays the gap out of its own
%        alist. Lily# reserves the key suffix in the same function as the meter's, so leaving
%        this unmeasured would have made the fix to it an assumption.
\book { \paper { indent = 0 ragged-right = ##f line-width = 60 }
  \score { \edgesweep "KEYONLY" { \key c \major \time 4/4 c'1 | c'1 | c'1 | \break \key a \major c'1 | c'1 | }
           \layout {} } }

% KEYCANC — a CANCELLING key and no meter, so the group is cancellation → signature and the
%        SIGNATURE is last. This is the shape the Lily# ledger point courtesy.key.key-to-line-end
%        is measured on, and it is here rather than only in KEYONLY because a cancellation is
%        where Lily#'s reservation stops being exact: it models the natural kerning as an
%        upper bound, so this score is the one that can show that bound's slack.
\book { \paper { indent = 0 ragged-right = ##f line-width = 60 }
  %% ONE measure before the break, so bar line #0 IS the break's — the index every other
  %% courtesy point in the ledger reads, and the Lily# twin can then be the same shape.
  \score { \edgesweep "KEYCANC" { \key ees \major \time 4/4 c'1 | \break \key a \major c'1 | }
           \layout {} } }

% BOTH — key AND meter, so the meter is last and the key is NOT. If both paid, the margin
%        would be 1.0; it is 0.5, which is what says the gap belongs to the group and not to
%        each member of it.
\book { \paper { indent = 0 ragged-right = ##f line-width = 60 }
  \score { \edgesweep "BOTH" { \key c \major \time 4/4 c'1 | c'1 | c'1 | \break \key a \major \time 3/4 c'2. | c'2. | }
           \layout {} } }

% -----------------------------------------------------------------------------------------
% WHAT THIS SECTION FOUND (2026-08-18, session 206)
%
% Line edge = STAFF right + 0.05 (the inset above). Every score below has line-width 60
% unless named otherwise, so its line edge is 34.143307.
%
%   score    last grob at line end   its ink right edge   line edge    margin
%   NONE     BarLine                     34.143307        34.143307    0.000000
%   NUM      TimeSignature 3/4           33.643307        34.143307    0.500000
%   COM      TimeSignature C             33.643307        34.143307    0.500000
%   W12      TimeSignature 12/8          33.643307        34.143307    0.500000
%   KEYONLY  KeySignature (3 sharps)     33.643307        34.143307    0.500000
%   BOTH     TimeSignature 3/4           33.643307        34.143307    0.500000
%   W40      TimeSignature 3/4           22.262205        22.762205    0.500000
%   W90      TimeSignature 3/4           50.714961        51.214961    0.500000
%   RAG      TimeSignature 3/4           25.339825        25.839825    0.500000
%
% ⇒ 0.500000 EXACTLY, across three ink widths, three line widths, ragged and justified, and
%   both the key path and the meter path. In BOTH the key does NOT pay it — only the last
%   member does. NONE is 0.000000, which is BarLine's own entry and not an absence of rule.
%
% ⇒ AND ONE THING THIS SECTION DELETED. KeySignature's ink for A major is 3.300030 =
%   3 x 1.100010, with nothing after it. Lily# had been adding a bare `+ 0.4` after the
%   signature — the FIFTH unnamed 0.4 in this end-of-line group, the same shape as the fourth
%   the 2026-08-03 note above describes, and standing in for this 0.5 badly. It is gone; the
%   entry is read once, from whichever grob is actually last.

