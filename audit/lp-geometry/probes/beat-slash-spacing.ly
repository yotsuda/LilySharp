\version "2.26.0"
%% LP FIDELITY PROBE — THE BEAT SLASH'S COLUMN (session 367).
%%
%% Produces the numbers in ../lp-geometry.json under the "percent.beat-slash.*" keys. Run it
%% with ../Measure-LilyPondProbe.ps1 -Probe beat-slash-spacing.ly and subtract the PROBE x's
%% by hand (every quantity is a difference of two anchors on ONE system).
%%
%% WHY THIS PROBE EXISTS. `\repeat percent N { <body shorter than a bar> }` engraves, for every
%% repetition, ONE grob and no notes: the iterator hands the Voice a RepeatSlashEvent in place
%% of the body (lily/percent-repeat-iterator.cc:75-92 next_element), and
%% Slash_repeat_engraver makes a RepeatSlash (slash-count >= 1) or a DoubleRepeatSlash
%% (slash-count 0, mixed durations) item from it (lily/slash-repeat-engraver.cc:56-66).
%% Both grobs carry rhythmic-grob-interface (scm/define-grobs.scm RepeatSlash /
%% DoubleRepeatSlash), so the column they stand in is a USED musical column with a
%% shortest-playing-duration of the body's length — and, the part Lily# had not read,
%% Note_spacing_engraver files a NoteSpacing wish for it exactly as for a note column
%% (lily/note-spacing-engraver.cc:87-91 acknowledge_rhythmic_grob). audit/lpreg/slashprobe.lys
%% read +1.1 ss too wide a bar against this music (HANDOFF §1 第361 ⑺⒞⑤), and the two
%% readings below are the whole of it.
%%
%% WHAT THE WISH DOES TO A COLUMN WITH NO HEAD (lily/note-spacing.cc:43-115 get_spacing):
%%   ideal = base.ideal_distance () - increment + left_head_end
%% where left_head_end is read off the wish's left NOTE COLUMNS (:46-70) — a RepeatSlash is
%% not one, so the term is 0 and the ideal is the bare duration space LESS the increment.
%% For a sixteenth-shortest bar (global_shortest 1/16, increment 1.2, shortest-duration-space
%% 2) the slash column's quarter is (2 + log2 4) x 1.2 = 4.8, and the wish makes it 3.6.
%% A column with NO wish (a skip) would have kept 4.8 with min 0 — the hemiola branch of
%% lily/spacing-spanner.cc:380-391 — which is what Lily# priced until session 367.
%%
%% THE ROD. The slash group's stencil is an ordinary element of its paper column, so its
%% ink enters the column's separation box with the default extra-spacing-width 0.1
%% (lily/separation-item.cc:152-187 boxes) and the rod to the NEXT column is padding 0.1 +
%% the box distance (lily/separation-item.cc:47-68 set_distance). Against a bar line whose
%% own box is its ink widened by 0.1, that is group ink + 0.3 — and 0.3 is exactly what the
%% dump shows between the DoubleRepeatSlash's last slash and the bar line.
%%
%% THE THREE READINGS, all on score BSL, all anchor differences (grob X in the system):
%%   percent.beat-slash.note-to-slash        f16 HEAD -> RepeatSlash        (the control: f's own
%%       wish names the slash column, so this is the ordinary sixteenth spring 2.4 - 1.2 +
%%       head 1.3042 = 2.504200, exact on both sides before the port)
%%   percent.beat-slash.slash-to-next-note   RepeatSlash -> g8. HEAD        (the wish with no
%%       head: 4.8 - 1.2 = 3.600000 — Lily# read 4.800000)
%%   percent.beat-slash.slash-to-barline     DoubleRepeatSlash -> BarLine   (the rod binds:
%%       group ink 3.757645 + 0.1 + 0.1 + 0.1 = 4.057645 — the ideal 3.6 is under it and
%%       Lily# read 4.800000)
%%
%% ⚠️ THE TWIN. LpGeometryProbes.cs BSL writes the SAME music in Lily#'s spelling — Lily# `c`
%% is LilyPond `c'` — as `repeat percent 2 { c16 d e f } repeat percent 2 { g8. c16 } | c1`.
%% Bar 2 is there so "the bar line" is an INTERIOR one, placed by its ink LEFT edge like every
%% barline.* point, and not the final one (ink RIGHT edge).
%%
%% ⚠️ ragged-right, so every spring is at its natural length and the readings are the
%% SPEC's; the rod reading is a MINIMUM that happens to exceed the ideal at force 0, which is
%% why it shows here at all.
%%
%% The dumps go to STDOUT; keep stderr on its own stream (Measure-LilyPondProbe.ps1 does).

\header { tagline = ##f }

#(define ((gd tag name) g)
   (format #t "\nPROBE ~a ~a x=~a ext=~a\n" tag name
           (ly:grob-relative-coordinate g (ly:grob-system g) X)
           (ly:grob-extent g g X)))

lay =
#(define-scheme-function (tag) (string?)
   #{
     \layout {
       ragged-right = ##t
       line-width = 500\mm
       indent = 0
       \context {
         \Score
         \override BarLine.after-line-breaking           = #(gd tag "BAR")
         \override NoteHead.after-line-breaking          = #(gd tag "HEAD")
         \override RepeatSlash.after-line-breaking       = #(gd tag "SLASH")
         \override DoubleRepeatSlash.after-line-breaking = #(gd tag "DSLASH")
       }
     }
   #})

%% BSL — two beat slashes in one bar: a plain two-slash RepeatSlash after four sixteenths
%%   (slash-count 2) and a dotted DoubleRepeatSlash after `g8. c16` (mixed durations,
%%   slash-count 0), then a bar line and a whole note.
\score { \new Staff { \time 4/4 \repeat percent 2 { c'16 d' e' f' } \repeat percent 2 { g'8. c'16 } | c'1 } \lay "BSL" }

%% ---------------------------------------------------------------------------------------
%% BST / BTT (session 367, leg 3) — THE SAME SLASH ON A TAB STAFF, where the sign is
%% one-and-a-half-sized: a TabStaff's staff-space is 1.5 and brew_slash scales every length
%% by Staff_symbol_referencer::staff_space (lily/percent-repeat-interface.cc:37-49), so the
%% dotted group is 5.636468 wide against the notation staff's 3.757645. The separation box
%% is per staff, and the rod over a column pair is the widest of them
%% (lily/spacing-spanner.cc:228-297 set_column_rods), so on a staff+tab system — the frame
%% every book of the owner's bass corpus is written in — the tab's group decides the bar
%% line's distance: DoubleRepeatSlash -> BarLine = 5.636468 + 0.1 + 0.1 + 0.1 = 5.936467,
%% on BST (staff + tab) and BTT (tab alone) alike. Lily# boxed the slash in the notation
%% frame only (4.057645) until this pair.
%%
%%   percent.beat-slash.tab-pair.slash-to-barline   BST: DoubleRepeatSlash -> BarLine
%%   percent.beat-slash.tab-only.slash-to-barline   BTT: the same, tab alone
%%
%% The slash -> next NOTE leg on a tab is NOT filed: it reads the next column's TabNoteHead
%% reach, and Lily#'s fret digits are its own, larger, cut (HANDOFF §3 F9) — that leg would
%% pin a decided divergence, not a port. Bass tuning, \tabFullNotation (Lily#'s default `tab`
%% is the full-notation one, LilyPondExporter writes it into every twin). Lily# `c,` is
%% LilyPond `c` under \clef bass.

m = { \time 4/4 \repeat percent 2 { c16 d e f } \repeat percent 2 { g8. c16 } | c1 }

\score { << \new Staff { \clef bass \m } \new TabStaff \with { stringTunings = #bass-tuning } { \tabFullNotation \m } >> \lay "BST" }
\score { \new TabStaff \with { stringTunings = #bass-tuning } { \tabFullNotation \m } \lay "BTT" }
