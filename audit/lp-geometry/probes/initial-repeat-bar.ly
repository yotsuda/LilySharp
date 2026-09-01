\version "2.26.0"
%% LP FIDELITY PROBE — the repeat bar line LilyPond does NOT print at the start of a piece.
%%
%% Produces the numbers in ../lp-geometry.json under "line-start.time-to-first-note.*".
%% Run it with ../Measure-LilyPondProbe.ps1 -Probe initial-repeat-bar.ly (about ten seconds).
%%
%% THE RULE. lily/bar-engraver.cc:432-449 Bar_engraver::pre_process_music, whose comment over pre_process_music reads "At the
%% start of a piece, we don't print any repeat bars": the repeatCommands loop that turns the
%% `start-repeat` posted by Repeat_acknowledge_engraver into `startRepeatBarType` is skipped
%% while first_time_ holds, i.e. while the Timing context is still at its first moment
%% (lily/bar-engraver.cc:414-417 Bar_engraver::initialize). The grob is never created, so it
%% costs no ink AND no width.
%%
%% WHY THE PAIR. "There is no opener at moment 0" is also what a book whose repeat was never
%% collected would say, and it is what a renderer that merely SKIPS DRAWING an opener whose
%% width it still reserved would NOT say. Both books below engrave the same two whole notes
%% after the same meter; IR wraps them in \repeat volta 2 and IN does not. The quantity is the
%% meter's right edge to the first note head, so:
%%   · if the opener is suppressed grob-and-all, IR and IN read the SAME number;
%%   · if it is suppressed at draw time only, IR is wider than IN by the reserved column;
%%   · if the repeat was never collected at all, the CLOSING `:|` of IR disappears too — which
%%     is why IR keeps its close and the Lily# twin's snapshot (test/initial-repeat-bar) shows
%%     an identical `|:` two bars later still being drawn.
%% LilyPond answers both, so the pair is anchored rather than self-referential.
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

%% IR — the piece OPENS with a repeat. LilyPond prints no opening bar line; the closing `:|`
%%   after the second whole note is printed as usual.
\score { \new Staff { \time 4/4 \repeat volta 2 { c'1 c'1 } } \lay "IR" }

%% IN — the control: the same two whole notes and the same meter, no repeat anywhere. Any
%%   difference between IN and IR is width the opener cost, and there must be none.
\score { \new Staff { \time 4/4 c'1 c'1 } \lay "IN" }
