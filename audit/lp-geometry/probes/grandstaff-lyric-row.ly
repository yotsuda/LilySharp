\version "2.26.0"
%% LP FIDELITY PROBE — A LYRIC ROW STANDING BETWEEN THE TWO STAVES OF A GRAND STAFF.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe grandstaff-lyric-row.ly -Prefix PROBEGS
%%
%% WHAT THIS OPENS (2026-08-28, session 274). HANDOFF §2 D has carried, since 2026-08-14
%% and marked 未着手, an item saying a lyric row inside a grandStaff stands somewhere else
%% in Lily# than in LilyPond, with this table:
%%
%%                     staff1 -> lyric baseline   lyric -> staff2   staff1 -> staff2
%%     LilyPond 2.26.0          6.739                  4.500             11.239
%%     Lily#                    5.650                  6.050             11.700
%%
%% TWO THINGS WERE TRUE OF THAT ITEM AND NEITHER WAS A MEASUREMENT: its .lys recipe no
%% longer parses (it spells `grandStaff { staff upper with lyrics words … }`, which is
%% LYS6011 today — a row inside a group is now a `lyrics NAME sings PART` item BETWEEN the
%% staff items), and no probe was ever kept, so the LilyPond column could not be re-read by
%% anyone. This file is that probe. It does NOT refute the 2026-08-14 reading — nothing can,
%% the book it was taken on is gone — it replaces it with one that has an observer.
%%
%% ⚠️ THE SHAPE IS NOT THE ONE ALREADY PINNED. Three families read a row near two staves and
%% none of them reads THIS one:
%%   - lyrics.between-staves.* (book LYRB, page-vertical.ly) is `<< Staff Lyrics Staff >>`
%%     with no group at all, and its first step is 4.027851;
%%   - lyrics.row-between.*   (book IOA, row-between-staves.ly) is an UNFOLDED row — one
%%     whose track name matches no part — and its first step is 5.653448;
%%   - lyrics.two-staff.*     (book LYRM) puts the row under the LAST staff, not between.
%% A GrandStaff puts the pair under StaffGrouper.staff-staff-spacing instead of
%% VerticalAxisGroup.default-staff-staff-spacing and draws a brace and through-bar-lines,
%% and Lily# reaches it by a different route (the group's own run). Whether that changes
%% the answer is exactly what was unmeasured.
%%
%% PREDICTION, written before running (HANDOFF 5.0-2). LilyPond's basic-distance is 9 on
%% both spacing specs, so GSN (the control, no row) should read 9.000000 EXACTLY and be a
%% positive control for the whole reading; with the row present the pair opens to whatever
%% the loose line's own chain solves to, and the SPLIT is what the item is about.
%% FALSIFIER for the control: anything but 9.000000 — that would mean the brace or the
%% through-bar-lines are in the reading and the pair is not measuring what it is named for.
%%
%% MEASURED (2026-08-28, 2.26.0, fonts pinned), refpoint to refpoint:
%%   GSL  staff1 -> lyric   5.021223442736809
%%        lyric  -> staff2  6.046346450541339
%%        staff1 -> staff2 11.067569893278148   (= the sum EXACTLY)
%%   GSN  staff1 -> staff2  9.000000000000000   (the control; the prediction held)
%%
%% ★★ AND THE READING IS ROBUST, WHICH IS WHY THE 6.739 / 4.500 COLUMN CANNOT BE A
%% SPELLING DIFFERENCE. Four variants were run before this file was written
%% (scratch/p274/gs-variants.ly, git-ignored): default paper with no \mark; the house font
%% pin WITH the exporter's \mark \markup \box; the \addlyrics spelling instead of an
%% explicit \new Lyrics \lyricsto; and staff2 taken OUT of the GrandStaff entirely. All
%% four print the same three numbers to every digit. LilyPond's answer here does not depend
%% on the paper, on the section label, on how the row is attached, or on whether the second
%% staff is in the group — so no re-spelling of this book produces 6.739 / 4.500 / 11.239.
%%
%% ★★ THE POINTS ARE ARMED, AND THE POISONS DECOMPOSE THE READING (HANDOFF 5.0-5: show the
%% poison alive BEFORE reading a small residual as agreement — otherwise "they agree" and
%% "nothing is measuring this" have the same face). Three poisons, each restored green:
%%
%%   SkylineDrop.RelatedStaffPadding    0.5 -> 0.55   staff-to-lyric  +0.050000000 (1:1)
%%                                                    staff-staff     +0.050000000 (1:1)
%%                                                    lyric-to-staff   UNMOVED
%%   SkylineDrop.UnrelatedStaffPadding  1.5 -> 1.55   lyric-to-staff  +0.050000000 (1:1)
%%                                                    staff-staff     +0.050000000 (1:1)
%%                                                    staff-to-lyric   UNMOVED
%%   StaffSpacingParameters.StaffStaff  9 -> 9.05     control         RED
%%                                                    GSL's three      UNMOVED
%%
%% The first two are ORTHOGONAL: each padding owns exactly one step and both reach the room,
%% which is the same statement the residuals make (room = the two steps' sum to every digit)
%% arrived at from the other side. The third is the one worth keeping: raising the pair's
%% basic-distance moves the CONTROL and leaves GSL alone, because GSL's pair is set by the
%% alignment walk (11.067570) and the spec never binds there. ⇒ a future change that made
%% the pair read its spec instead of its walk would go red on GSL and stay green on GSN.

%% ⚠️ THE PITCHES. Lily# absolute is LilyPond minus one apostrophe (probe trap 5). The frame
%% below is not hand-converted: it is what `lysc ly` emitted for the .lys twin (\fixed c'
%% with the note spellings verbatim), and only the Lyrics context is hand-added — the twin
%% exporter drops lyric rows (HANDOFF §1, "双子 exporter は和音行と歌詞行を出さない"), which
%% is why a lyric probe cannot be round-tripped and has to say so.
%%
%% Everything printed is in STAFF SPACES (see page-vertical.ly's header for why).

#(define (dump tag layout pages)
   (for-each
    (lambda (page)
      (for-each
       (lambda (sys)
         (let ((sg (ly:prob-property sys 'system-grob)))
           (if (ly:grob? sg)
               (let ((all (ly:grob-object sg 'all-elements)))
                 (if (ly:grob-array? all)
                     (for-each
                      (lambda (g)
                        (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                          (if (memq nm '(VerticalAxisGroup StaffSymbol))
                              (format #t "PROBEGS ~a ~a rel=~a ext=(~a . ~a) aff=~a\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))
                                      (ly:grob-property g 'staff-affinity 'none)))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))

probeGS =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.serif = "LilyPond Serif"
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEGS BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})

upper = \fixed c' {
  \time 4/4
  d'4 d' e' d' |
  b4 a b2 |
}

lower = \fixed c' {
  \time 4/4
  g,4 b, c a, |
  d4 d d2 |
}

%% GSL — the row between the two staves of the group.
\book {
  \probeGS "GSL"
  \score {
    \new GrandStaff <<
      \new Staff \new Voice = "mel" { \clef "treble" \upper }
      \new Lyrics \lyricsto "mel" { Praise God from whom all bless -- ings }
      \new Staff { \clef "bass" \lower }
    >>
  }
}

%% GSN — GSL with the row REMOVED and nothing else changed. The positive control: the pair
%% must read the spring's ideal 9.000000, so a residual on GSL is the row's and only the
%% row's. (HANDOFF 5.0: the strongest pairs are the ones LilyPond makes an identity; this
%% one is the weaker kind — removing a line is not an identity — but the control's job here
%% is to prove the READING, not to price the term.)
\book {
  \probeGS "GSN"
  \score {
    \new GrandStaff <<
      \new Staff \new Voice = "mel" { \clef "treble" \upper }
      \new Staff { \clef "bass" \lower }
    >>
  }
}
