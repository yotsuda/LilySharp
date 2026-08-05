\version "2.26.0"
%% LP FIDELITY PROBE — where LilyPond puts the accidentals of a staff column that TWO VOICES
%% stand on, and which voice the seconds collision displaces.
%%
%% TWO SETS OF BOOKS, and they answer to different readers.
%%   XVA..XVH  the ORIGINAL measurement, in the plain `GAP` dump, on a bare score. These are
%%             the numbers quoted in StaffAccidentalColumns, NoteCollision, the unit tests and
%%             the two commit messages. Their absolute x's are quoted, so do not re-lay them
%%             out — a citation that no longer reproduces is worse than none (HANDOFF 5.2).
%%   XCA..XCH  the LEDGER books: the same music under the ledger's own layout (ragged-right,
%%             line-width 500mm, indent 0, two measures) and the `PROBE ` dump that
%%             ../Measure-LilyPondGeometry.ps1 parses. Their absolute x's differ from the
%%             XV set — the layout is different — but every DIFFERENCE the ledger records is
%%             identical to its XV counterpart, which is the point: the quantities
%%             note-collision and position_apes decide do not read the line width. That
%%             agreement is itself checked, book by book, in the table further down.
%%
%% WHY. Lily# solved position_apes once per ITEM, so each voice packed its accidentals against
%% its own head only, and then the note-collision shift carried the accidental along with the
%% head — landing the shifted voice's accidental on top of the other voice's notehead
%% (scratch/ベースタブLy/collision.lys, `voice { aes' … } { ges' … }`). In LilyPond there is ONE
%% AccidentalPlacement grob per staff moment: accidental-placement.cc:479-518
%% calc_positioning_done, whose extract_heads_and_stems (:303-355) walks the note columns of
%% EVERY accidental it holds and takes their heads at their real (collision-shifted) X, whose
%% build_heads_skyline (:375-385) makes one reference skyline out of all of them, and whose
%% position_apes (:391-438) stacks the whole set right-to-left. The grob is not inside the note
%% column note-collision.cc translates, so the accidentals do not ride the shift.
%%
%% Run (Guile deadlocks on an inherited console, so detach with < NUL):
%%   cmd /d /s /c "lilypond -dno-point-and-click -o out cross-voice-accidental.ly > out.txt 2>&1 < NUL"
%%
%% EXPECTED (LilyPond 2.26.0 — the values Lily# is measured against). x is the grob origin in
%% the system's frame; a flat's extent is (-0.12 . 0.80), a black head's (0 . 1.3042).
%%
%%   XVA  << { aes' } \\ { g' } >>    heads pos-1 9.059735 · pos-2 10.363935
%%                                    ONE flat (the UPPER, shifted voice's) at 7.909735
%%   XVB  << { a' } \\ { ges' } >>    heads pos-1 9.059735 · pos-2 10.363935
%%                                    ONE flat (the LOWER, pinned voice's) at 7.909735
%%   XVC  << { aes' } \\ { ges' } >>  heads pos-1 10.126351 · pos-2 11.430551
%%                                    flats 8.976351 (upper) · 7.909736 (lower)
%%   XVD  <ges' aes'>  (ONE voice)    heads pos-2 10.095351 · pos-1 11.334551
%%                                    flats 7.909736 (lower) · 8.976351 (upper)
%%
%% ★ XVA and XVB put the SAME flat at the SAME x whichever voice carries it — 0.35 left of the
%%   LEFTMOST head (9.059735 - 0.35 - 0.80), never of its own. That is the whole claim.
%% ★ XVC and XVD agree to fourteen digits: a staff column's accidentals are packed exactly as
%%   one chord's are, so the packing has no second algorithm — Lily# reuses AccidentalPlacement.
%% ★★ XVA/XVB ALSO pin which voice moves: the UP-stem head is at 9.059735 (pos -1 = a'/aes')
%%   and the DOWN-stem head at 10.363935 (pos -2 = g'/ges'), one head width (1.3042) right.
%%   LilyPond displaces the DOWN-stem voice and leaves the up-stem one on the column —
%%   note-collision.cc:212-227 takes the `touch` branch (shift_amount = -1) for a SECOND, and
%%   :323-324 multiplies it by 0.5, so calc_positioning_done's `amount - left_most` moves the
%%   down group by 1.0 head width. ⚠️ Lily# instead moves the UP voice right by 1.04 head
%%   widths (its close_half branch, 0.52 each way) — a SEPARATE divergence from the accidental
%%   packing, still open. It is why Lily# reads 0.03 less than XVA's 0.35 here: with the wrong
%%   head pinned, the flat's bowl binds against a different Y band of the heads' skyline.
%%   Book XVE is that collision on its own, with no accidental to confuse it.

#(define (rec name)
   (lambda (grob)
     (let* ((sys (ly:grob-system grob))
            (x (ly:grob-relative-coordinate grob sys X))
            (sp (ly:grob-property grob 'staff-position))
            (ext (ly:grob-extent grob grob X)))
       (format #t "\nGAP ~a pos=~a x=~a extL=~a extR=~a\n" name sp x (car ext) (cdr ext)))
     '()))

%% (1) Only the UPPER (displaced) voice carries an accidental.
\book { \score { \new Staff \with {
  \override NoteHead.after-line-breaking = #(rec "XVA-HEAD")
  \override Accidental.after-line-breaking = #(rec "XVA-ACC")
} { \time 2/4 << { aes'4 r4 } \\ { g'4 r4 } >> }
  \layout { indent = 0\mm } } }

%% (2) Only the LOWER (pinned) voice carries an accidental.
\book { \score { \new Staff \with {
  \override NoteHead.after-line-breaking = #(rec "XVB-HEAD")
  \override Accidental.after-line-breaking = #(rec "XVB-ACC")
} { \time 2/4 << { a'4 r4 } \\ { ges'4 r4 } >> }
  \layout { indent = 0\mm } } }

%% (3) Both voices, a second apart — the reported case.
\book { \score { \new Staff \with {
  \override NoteHead.after-line-breaking = #(rec "XVC-HEAD")
  \override Accidental.after-line-breaking = #(rec "XVC-ACC")
} { \time 2/4 << { aes'4 r4 } \\ { ges'4 r4 } >> }
  \layout { indent = 0\mm } } }

%% (4) The SAME two pitches as a chord in one voice — the control for (3).
\book { \score { \new Staff \with {
  \override NoteHead.after-line-breaking = #(rec "XVD-HEAD")
  \override Accidental.after-line-breaking = #(rec "XVD-ACC")
} { \time 2/4 <ges' aes'>4 r4 }
  \layout { indent = 0\mm } } }

%% (5) The bare seconds collision, no accidentals: WHICH head moves, and by how much.
%%     EXPECTED heads pos-1 8.489735 · pos-2 9.793935 (difference 1.304200 = one head width).
\book { \score { \new Staff \with {
  \override NoteHead.after-line-breaking = #(rec "XVE-HEAD")
} { \time 2/4 << { a'4 r4 } \\ { g'4 r4 } >> }
  \layout { indent = 0\mm } } }

%% (6)-(8) The rest of check_meshing_chords's branch ORDER, which is what makes (5) come out
%% that way. `touch` is decided at :68-75 and consumed at :212 and :323 — BEFORE
%% close_half_collide (:325) and full_collide (:327) — so a second and a unison both take the
%% touch branch (shift_amount = -1, then ×0.5) and it is the DOWN-stem voice that moves right.
%% The 0.52 / 0.5 / 0.65 multipliers are only reached when `touch` is false.
%%
%% XVF  unison, half over quarter (no merge: different heads)  -> touch, DOWN moves right
%% XVG  unison, half over DOTTED quarter (up.dots < down.dots) -> :202-211 fires, but
%%      `if (!touch) stem_to_stem = true` does NOT, because the extremes touch — so this is
%%      still ×0.5 and NOT the 0.65 stem_to_stem.
%% XVH  <e' g'> half over a dotted quarter g' — the up group's LOWEST head is a third below
%%      the down head, so `touch` is false and :202-211 DOES set stem_to_stem: ×0.65.
\book { \score { \new Staff \with {
  \override NoteHead.after-line-breaking = #(rec "XVF-HEAD")
} { \time 2/4 << { g'2 } \\ { g'4 r4 } >> }
  \layout { indent = 0\mm } } }

\book { \score { \new Staff \with {
  \override NoteHead.after-line-breaking = #(rec "XVG-HEAD")
} { \time 2/4 << { g'2 } \\ { g'4. r8 } >> }
  \layout { indent = 0\mm } } }

\book { \score { \new Staff \with {
  \override NoteHead.after-line-breaking = #(rec "XVH-HEAD")
} { \time 2/4 << { <e' g'>2 } \\ { g'4. r8 } >> }
  \layout { indent = 0\mm } } }

%% ===========================================================================================
%% THE LEDGER BOOKS. Same music, the ledger's layout, and the `PROBE ` dump
%% ../Measure-LilyPondGeometry.ps1 reads. Each has a twin in
%% LilySharp.Tests/LpFidelity/LpGeometryProbes.cs engraving the SAME music — mind the octave
%% convention: Lily# `c` is LilyPond `c'`.
%%
%% Every book is TWO measures: a plain first bar so there is an unambiguous bar line, and the
%% column under test opening the second. The ledger reads DIFFERENCES after that bar line,
%% never absolute x, so the layout below cannot enter a recorded number.
%%
%% EXPECTED (LilyPond 2.26.0), and — the reason these books exist twice — every one of them
%% is its XV counterpart's difference to six digits, under a different line width:
%%
%%   crossvoice.accidental.shifted-voice-to-head   XCA  1.150000  = XVA  9.059735 - 7.909735
%%   crossvoice.accidental.pinned-voice-to-head    XCB  1.150000  = XVB  9.059735 - 7.909735
%%   crossvoice.accidental.column-gap              XCC  1.066615  = XVC  8.976351 - 7.909736
%%   crossvoice.collision.second                   XCE  1.304200  = XVE  9.793935 - 8.489735
%%   crossvoice.collision.unison-half-over-quarter XCF  1.377400  = XVF  9.867135 - 8.489735
%%   crossvoice.collision.stem-to-stem             XCH  1.790620  = XVH 10.280355 - 8.489735
%%
%% ⚠️ XCA and XCB are a MIRROR PAIR and must agree, exactly as CSB/CSA and CFB/CFA do: the
%% accidental is 0.35 clear of the LEFTMOST head whichever voice carries it, so a difference
%% between the two is a defect on its own and not a rounding.

#(define ((gd tag name) g)
   (format #t "\nPROBE ~a ~a x=~a ext=~a\n" tag name
           (ly:grob-relative-coordinate g (ly:grob-system g) X)
           (ly:grob-extent g g X)))

xlay =
#(define-scheme-function (tag) (string?)
   #{
     \layout {
       ragged-right = ##t
       line-width = 500\mm
       indent = 0
       \context {
         \Score
         \override BarLine.after-line-breaking    = #(gd tag "BAR")
         \override NoteHead.after-line-breaking   = #(gd tag "HEAD")
         \override Accidental.after-line-breaking = #(gd tag "ACC")
       }
     }
   #})

%% XCA — only the UPPER (displaced) voice carries the accidental.
\book { \score { \new Staff { \time 4/4 c'4 d' e' f' |
  << { aes'4 r4 r4 r4 } \\ { g'4 r4 r4 r4 } >> } \xlay "XCA" } }

%% XCB — only the LOWER (pinned) voice carries it. The mirror of XCA.
\book { \score { \new Staff { \time 4/4 c'4 d' e' f' |
  << { a'4 r4 r4 r4 } \\ { ges'4 r4 r4 r4 } >> } \xlay "XCB" } }

%% XCC — both voices, a second apart: the two accidental columns.
\book { \score { \new Staff { \time 4/4 c'4 d' e' f' |
  << { aes'4 r4 r4 r4 } \\ { ges'4 r4 r4 r4 } >> } \xlay "XCC" } }

%% XCE — the bare seconds collision, no accidentals.
\book { \score { \new Staff { \time 4/4 c'4 d' e' f' |
  << { a'4 r4 r4 r4 } \\ { g'4 r4 r4 r4 } >> } \xlay "XCE" } }

%% XCF — a unison, half over quarter: the displacement is the UP head's ink, not the down one's.
\book { \score { \new Staff { \time 4/4 c'4 d' e' f' |
  << { g'2 r2 } \\ { g'4 r4 r2 } >> } \xlay "XCF" } }

%% XCH — <e' g'> half over a dotted quarter g': the extremes do NOT touch, so this is the one
%% book on the list that reaches stem_to_stem (0.65). Three heads stand after the bar line, so
%% the reading is the column's WIDEST head-to-head span, not "the first two".
\book { \score { \new Staff { \time 4/4 c'4 d' e' f' |
  << { <e' g'>2 r2 } \\ { g'4. r8 r2 } >> } \xlay "XCH" } }
