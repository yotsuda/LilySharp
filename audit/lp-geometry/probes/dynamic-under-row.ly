\version "2.26.0"
%% LP FIDELITY PROBE — DOES A ROW LEADING THE *NEXT* SYSTEM MAKE ROOM FOR THAT SYSTEM'S
%% FIRST STAFF'S OUTSIDE-STAFF INK?
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe dynamic-under-row.ly -Prefix PROBEDU
%%
%% WHAT THIS OPENS (2026-08-29, session 291). This is the BETWEEN-SYSTEMS half of the
%% question textspanner-under-row.ly opened and session 286 closed on one path only. That
%% pair measured a row standing above a staff INSIDE ONE SYSTEM, and the repair went into
%% the room (MultiStaffLayouter.BuildAllStaffSkylines merges the spanner's ink into the
%% staff's UP profile). A row that LEADS A LATER SYSTEM is not placed by the room: it is an
%% occupant of the previous block's loose-line chain, and that chain closes on the next
%% system's first spaceable staff through LayoutEngine.LeadingLinesOfSystem — which reads
%% the staff's INSIDE-staff silhouette (SkylineBuilder.BuildInsideStaffSkylines), plus one
%% hand-merged special case for the text spanner that session 286 added there.
%% ⇒ SO THE TWO ENDS OF ONE CHAIN READ TWO DIFFERENT PROFILES: the between-staves end
%% (LayoutEngine.ComputeBetweenStavesEnd) takes the room's own per-staff skyline — dynamics
%% and all — and the between-systems end takes the inside one. HANDOFF 5.2.1 (2).
%%
%% WHAT LILYPOND DOES, read in the source before this probe was written:
%%   * the loose line's minimum against the closing staff is min_offsets[k-1] - min_offsets[k]
%%     (page-layout-problem.cc:961-962, :923-925), out of Align_interface's own walk;
%%   * that walk reads each element's `vertical-skylines` (align-interface.cc:207 get_skylines);
%%   * a VerticalAxisGroup's `vertical-skylines` is Axis_group_interface::skyline_spacing
%%     (axis-group-interface.cc:860-985), which merges the inside-staff skylines AND every
%%     PLACED outside-staff grob.
%% ⇒ so a DynamicText forced above the staff should be in the distance. THAT IS THE CLAIM
%% THIS PAIR EXISTS TO TEST, not to assume.
%%
%% THE PAIR (one variable):
%%   DUR — ChordNames row / Staff / Lyrics, two systems, with \f forced ABOVE the staff on
%%         the first note of SYSTEM 2 — i.e. under the row that leads system 2, at the same
%%         x as that row's first symbol.
%%   DUN — THE CONTROL: DUR with the ^\f removed and nothing else changed.
%%
%% ⚠️ THE INK IS AN EMMENTALER GLYPH ON PURPOSE. textspanner-under-row.ly had to pin
%% fonts.serif because its ink was the word "rit."; a dynamic's ink is a music-font glyph
%% that both engines take from the same file, so the pair's difference cannot be a text
%% metric. The font pins are kept anyway for the chord symbols and the syllables, which are
%% not what is being read but do set where the row's own baseline sits.
%%
%% ⚠️ THE LYRICS LINE IS NOT DECORATION. Lily#'s BuildLooseChainEnds declines a score with
%% no lyric line at all, so a book without one would never reach the branch under test and
%% the pair would measure a different (already-named) defect instead — the row left at
%% force 0. The row is what is READ; the lyrics are what make the chain run.
%%
%% THE MUSIC IS QUIET ON PURPOSE: drawn third-space c'' throughout, so the dynamic rests on
%% its own staff-padding floor rather than on a note column that moves when the pitches do.
%% ⚠️ Lily# `c'` is LilyPond `c''` here (HANDOFF 5.5); the twin's body came out of
%% `lysc ly` on scratch/p291/dur.lys, not out of a head.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2), with signs and an arithmetic.
%% LilyPond's DynamicLineSpanner declares outside-staff-priority 250, staff-padding 0.1,
%% padding 0.6 and minimum-space 1.2 (scm/define-grobs.scm). On quiet third-space music the
%% staff's own up-ink is its top line, 2.05, so the placed glyph's ink bottom is
%% max(2.05 + 0.1, 1.2) = 2.15 and its ink top is 2.15 + (the \f glyph's height above its
%% own baseline's reference).
%%   (a) IF the control's distance is skyline-bound on the staff's own top ink, then
%%           DUR - DUN = 0.1 + (the glyph's ink height)  — strictly positive, and the
%%       arithmetic is checkable against the DynamicText ext printed below.
%%   (b) IF the control's distance is set instead by the spring's rest length (the skyline
%%       slack being larger), the increase is SMALLER than that, and it is 0 if the glyph
%%       still fits under the rest length. Then this pair says the mechanism is real but
%%       this arrangement cannot see it, and the pair has to be re-cut with taller ink.
%%   EITHER WAY THE SIGN IS ASSERTED: DUR - DUN >= 0, and (a) says > 0.
%% ⚠️ FALSIFIER, and it is a real one: DUR == DUN to every digit while the glyph is
%% demonstrably drawn (the DynamicText line below proves it exists) would mean LilyPond does
%% NOT let a placed outside-staff grob push the loose line above it — that skyline_spacing's
%% merge does not reach the loose-line chain. Lily#'s current behaviour would then be
%% FAITHFUL and this item dies here. DO NOT PORT ANYTHING BEFORE READING THESE NUMBERS.
%%
%% ⚠️ AND THE SECOND PREDICTION IS ABOUT LILY#, kept separate from the one about LilyPond
%% (HANDOFF 1, session 291 (3)): Lily# reads DUR == DUN EXACTLY, because the closing staff's
%% profile at this call site is the inside one and a dynamic is not in it. Measured before
%% this probe was written, on the same arrangement with a text label instead of the glyph:
%% the chord row of system 2 sat at the same place with and without the label, and printed
%% through it (scratch/p291/crt.lys — label baseline 20.71, chord baseline 21.41, x 6.0..7.6
%% shared).
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
                          (if (memq nm '(VerticalAxisGroup StaffSymbol DynamicText))
                              (format #t "PROBEDU ~a ~a rel=~a ext=(~a . ~a) aff=~a\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))
                                      (ly:grob-property g 'staff-affinity 'none)))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))

probeDU =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.serif = "LilyPond Serif"
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEDU BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})

%% ⚠️ NOT `chords`: that is a LilyPond keyword and the parser says so.
chordRow = \chordmode { d1 | e1 | d1 | e1 }
words = \lyricmode { no no no no no no no no no no no no no no no no }

quiet = \fixed c' {
  \time 4/4
  \key c \major
  c'4 c' c' c' |
  c'4 c' c' c' |
  \break
  c'4 c' c' c' |
  c'4 c' c' c' \bar "|."
}

loud = \fixed c' {
  \time 4/4
  \key c \major
  c'4 c' c' c' |
  c'4 c' c' c' |
  \break
  c'4^\f c' c' c' |
  c'4 c' c' c' \bar "|."
}

%% DUR — the dynamic sits above system 2's staff, under the row that leads system 2.
\book {
  \probeDU "DUR"
  \score {
    <<
      \new ChordNames \chordRow
      \new Staff { \new Voice = "mel" \loud }
      \new Lyrics \lyricsto "mel" \words
    >>
  }
}

%% DUN — THE CONTROL: DUR with the ^\f removed and nothing else changed.
\book {
  \probeDU "DUN"
  \score {
    <<
      \new ChordNames \chordRow
      \new Staff { \new Voice = "mel" \quiet }
      \new Lyrics \lyricsto "mel" \words
    >>
  }
}
