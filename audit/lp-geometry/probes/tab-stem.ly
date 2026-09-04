\version "2.26.0"
%% LP FIDELITY PROBE — AN UNBEAMED STEM ON A FULL-NOTATION TAB STAFF (session 337).
%%
%% WHY THIS EXISTS. The corpus reads a tab staff's PLACEMENT (page-vertical.ly TABL) and its
%% BEAM (tab-slur / the T7 fixtures), but never the length of an UNBEAMED tab stem — because
%% the default TabStaff draws none (engraver-init.ly:1258 Stem.stencil = ##f). Under
%% \tabFullNotation the Stem prints again (property-init.ly:832), bought with the ORDINARY
%% stem machinery: details.lengths by duration, the unnatural-direction shortening, the pull
%% to the middle line — all in the tab's own spaces, whose space is the string gap. Lily#
%% drew a flat 3.0 × string-space from the digit instead, so every full-notation tab system
%% stood ~1.45 ss too tall and paged wrong (the owner's Sugar: 3 pages where LilyPond makes
%% 2). This pins the stem's TIP to LilyPond so the port is anchored, not asserted.
%%
%% THE FRAME. A stem's staff-position is measured in HALF the staff's own space from the
%% middle line, up-positive — the same frame `positions` speaks. On a six-string guitar tab
%% the strings sit at positions +5 +3 +1 −1 −3 −5 (StaffPositionOfString: count+1−2·string),
%% and the middle (position 0) is the gap between strings 3 and 4.
%%
%% THE MUSIC. Single unbeamed eighths (a flag, no beam) on chosen strings so both the natural
%% direction and the shortening are exercised:
%%   * a'8 on string 2 (position +3) — upper half → stem DOWN, its head above the middle, so
%%     the down-stem points the way the head already lies and IS shortened (stem.cc:519-522).
%%   * e8 on string 6 (position −5) — lower half → stem UP, likewise shortened on its side.
%%   * b8 on string 3 (position +1) — just above the middle → DOWN, shortened.
%% Each is followed by rests so nothing beams. autoBeamOff for safety.
%%
%% PROBETAB head=<head staff-position> dir=<1 up / -1 down> begin=<stem-begin-position>
%%          end=<stem end = begin + dir*length>   (all in half-tab-spaces above the middle)
%%   The end is LilyPond's stem_end (stem.cc:588 hp[dir] + dir*length); the begin is where
%%   the visible stem starts, on the digit's far edge (calc_tab_stem_attachment, ±1.35 of the
%%   head half-height). A rest's invisible stem prints head=none.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe tab-stem.ly -Prefix PROBETAB

\layout {
  \context {
    \Score
    \override Stem.after-line-breaking =
      #(lambda (grob)
         (let* ((ss (ly:grob-object grob 'staff-symbol))
                (heads (ly:grob-array->list (ly:grob-object grob 'note-heads))))
           (if (and (ly:grob? ss) (pair? heads))
               (let ((hp (ly:grob-property (car heads) 'staff-position 'none))
                     (dir (ly:grob-property grob 'direction 1))
                     (begin (ly:grob-property grob 'stem-begin-position 0))
                     (len (ly:grob-property grob 'length 0)))
                 (format #t "\nPROBETAB head=~a dir=~a begin=~a end=~a\n"
                         hp dir begin (+ begin (* dir len)))))))
  }
}

%% The string is pinned on BOTH sides (\1) so the head position is not left to a fret search:
%% on a six-string guitar string 1 is staff-position +5 (upper half → stem DOWN, on the side
%% away from the head, so NOT shortened). One eighth, followed by rests so nothing beams.
\score {
  \new TabStaff \with { \tabFullNotation autoBeaming = ##f } {
    e''8\1 r8 r2. |
  }
}
