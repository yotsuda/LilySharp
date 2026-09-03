\version "2.26.0"
%% LP FIDELITY PROBE — the repeat bar line at the start of a piece, PRINTED.
%%
%% Produces the numbers in ../lp-geometry.json under "line-start.time-to-first-note.*".
%% Run it with ../Measure-LilyPondProbe.ps1 -Probe initial-repeat-bar.ly (about ten seconds).
%%
%% THE RULE, AND THE DECISION OVER IT. By default LilyPond prints no automatic repeat bar at
%% the start of a piece: lily/bar-engraver.cc:432-449 Bar_engraver::pre_process_music, whose
%% comment over pre_process_music reads "At the start of a piece, we don't print any repeat
%% bars" — the repeatCommands loop that turns Repeat_acknowledge_engraver's `start-repeat`
%% into `startRepeatBarType` is skipped while first_time_ holds (lily/bar-engraver.cc:414-417
%% Bar_engraver::initialize). LilyPond keeps a door open for the lead-sheet convention:
%% `\set Score.printInitialRepeatBar = ##t` (Documentation/en/notation/repeats.itely:160-172).
%%
%% Lily# PRINTS it (owner decision, session 328): a `|:` in a .lys is always one the writer
%% wrote, and the corpus is lead sheets. Every Lily# twin therefore carries the setting
%% (LilyPondExporter, in \layout), and so does IR below — the pair measures the OPENER'S
%% WIDTH, LilyPond's against Lily#'s, instead of its absence. Until session 328 IR carried no
%% setting and read 3.700000, the same as IN: that number pinned the ported gate (session 319).
%%
%% WHY THE PAIR. Both books engrave the same two whole notes after the same meter; IR opens
%% with \repeat volta 2 and prints the opener, IN has no repeat anywhere. The quantity is the
%% meter's right edge to the first note head, so IR − IN is the width the printed opener costs
%% — the column LilyPond reserves for `.|:` at a line start — and IN pins that the metered line
%% start itself did not move.
%%
%% THE COLUMN, SPLIT. IR's BAR line is a third point (line-start.time-to-repeat-bar): at a line
%% start the staff-bar comes AFTER the meter (scm/define-grobs.scm:668-683, begin-of-line
%% order), at TimeSignature's (staff-bar . (extra-space . 1.0)); the head then stands off the
%% BAR's ink by BarLine's (first-note . (semi-shrink-space . 1.3)). Dumped on 2.26.0:
%% TIME x=4.885 ext 1.7, BAR x=7.585 ext 1.84, HEAD x=10.725 — 2.700000 and 5.840000.
%% ID below adds the fourth point: the same column with a down stem after the opener
%% (line-start.time-to-first-note.initial-repeat.down-stem, HEAD x=10.867857).
%%
%% ragged-right, like every other X probe here: force 0, so this reads the spring's ideal
%% rather than a share of some line's stretch (see barline-spacing.ly's header).

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
         \override BarLine.after-line-breaking       = #(gd tag "BAR")
         \override NoteHead.after-line-breaking      = #(gd tag "HEAD")
         \override Clef.after-line-breaking          = #(gd tag "CLEF")
         \override TimeSignature.after-line-breaking = #(gd tag "TIME")
       }
     }
   #})

%% IR — the piece OPENS with a repeat, and the opener is printed, as every Lily# twin asks.
\score { \new Staff { \set Score.printInitialRepeatBar = ##t \time 4/4 \repeat volta 2 { c'1 c'1 } } \lay "IR" }

%% IN — the control: the same two whole notes and the same meter, no repeat anywhere. The
%%   difference between IR and IN is the width the printed opener costs.
\score { \new Staff { \time 4/4 c'1 c'1 } \lay "IN" }

%% ID — the opener followed by a DOWN stem. Staff_spacing::get_spacing adds
%%   next_notes_correction (lily/staff-spacing.cc:206-208) to both fixed and ideal when the
%%   extremal grob is a bar line: for a down stem, min(overlap with the bar / 7, 1) times
%%   StaffSpacing's stem-spacing-correction 0.4. The same four notes read 1.042857 mid-line
%%   (barline-spacing probes, head pos 6: overlap 2.5 → 0.142857); at a LINE START the bar is
%%   the opener and the head should stand 10.725 + 0.142857 = 10.867857 from the left edge.
\score { \new Staff { \set Score.printInitialRepeatBar = ##t \time 4/4 \repeat volta 2 { a''4 b'' c''' d''' a''4 b'' c''' d''' } } \lay "ID" }
