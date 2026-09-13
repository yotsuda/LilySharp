\version "2.26.0"
%% LP FIDELITY PROBE — the cross-voice column ROD reads the PAPER column's skylines (padding 0.08).
%%
%% WHY THIS PROBE EXISTS (session 379). Two separation items stand on one column and LilyPond pads
%% each by its own skyline-vertical-padding (lily/separation-item.cc:92-110 calc_skylines):
%%   scm/define-grobs.scm:2577 NoteColumn  0.15 — the spacing WISH's minimum (note-spacing.cc:78-83)
%%   scm/define-grobs.scm:2747 PaperColumn 0.08 — the ROD (separation-item.cc:47-68 set_distance)
%% Lily# padded every view 0.15, the rod's included. On test/multivoice-tuplet-beams voice 2's f''
%% (an eighth at 1/8) and voice 1's ledgered triplet c''' (at 1/6) are a pair no voice spans, so only
%% the rod floors them; LilyPond's paper-column skylines miss each other by 0.135 in Y and leave the
%% bare duration ideal, 0.80, where Lily#'s 0.15-padded ones overlapped and held the pair at 1.4492.
%%
%% SCORE CVR: one pitch per voice, so the Lily# reading can group heads by Y —
%%   voice 1: c'''4 triplets then c'''2  (staff position 8, two ledger lines, stems up)
%%   voice 2: eight f''8                 (staff position 4, the top line, stems down)
%% READING: the second c''' (1/6) less the second f'' (1/8). Lily# twin: LpGeometryProbes.cs, score
%% CVR (Lily# octave absolute `c''` = LilyPond `c'''`, `f'` = `f''`).
%%
%% Dumps go to STDOUT, ONE RECORD PER HEAD:
%%   PROBE CVR HEAD pos=<staff position> x=<x on the system>

\header { tagline = ##f }

\layout {
  indent = 0
  ragged-right = ##t
  \context {
    \Score
    \override NoteHead.after-line-breaking =
      #(lambda (g)
         (format #t "\nPROBE CVR HEAD pos=~a x=~,6f\n"
                 (ly:grob-property g 'staff-position)
                 (ly:grob-relative-coordinate g (ly:grob-system g) X)))
  }
}

\score {
  \new Staff { \time 4/4
    << { \tuplet 3/2 { c'''4 c''' c''' } c'''2 } \\ { f''8 f'' f'' f'' f'' f'' f'' f'' } >>
    \bar "|." }
}
