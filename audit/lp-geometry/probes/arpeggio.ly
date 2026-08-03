\version "2.26.0"

%% HOW WIDE IS AN ARPEGGIO, AND HOW FAR DOES IT STAND FROM ITS CHORD?
%%
%% LilyPond's arpeggio is a STACK OF GLYPHS, and both readings follow from that:
%%
%%   lily/arpeggio.cc:34-41 get_squiggle      the wiggle IS the font glyph `scripts.arpeggio`,
%%                                            fetched by name from the grob's default font.
%%   lily/arpeggio.cc:313-319 Arpeggio::width the grob's X-extent is THAT GLYPH'S extent —
%%                                            `X-extent` is this callback
%%                                            (scm/define-grobs.scm:218), so the wiggle's
%%                                            width is a font metric and nothing else.
%%   lily/arpeggio.cc:180-183 print           the stencil is squiggles stacked upward
%%                                            (`add_at_edge`) until the pile is as long as
%%                                            the head interval.
%%   scm/define-grobs.scm:208-221             (direction . LEFT) (side-axis . X)
%%                                            (padding . 0.5) and
%%                                            X-offset = side-position-interface::x-aligned-side,
%%                                            so the wiggle's RIGHT edge stands `padding`
%%                                            left of the note column's LEFT edge. Both edges
%%                                            are grob extents, i.e. glyph ink.
%%
%% ⚠️ LILY# DRAWS THE WIGGLE INSTEAD OF SETTING IT. SharedRenderer.DrawArpeggios walks a
%% parabola in 4 subdivisions per half wave and emits line segments, half an amplitude
%% (ArpeggioEngraver.WaveAmplitude = 0.2) either side of the centre — so the width is a
%% number the renderer chose, not the glyph's. That is the first reading.
%%
%% ⚠️ AND THE PLACEMENT IS THE SECOND. ArpeggioEngraver takes the column X and subtracts a
%% HALF HEAD WIDTH (GetNoteheadBBox(nv).CenterX) before the padding, on the stated ground
%% that the column X is the head's CENTRE. These two books say whether that is so: they are
%% the same chord at the same pitches, and the only thing that changes is the HEAD SHAPE —
%% quarter (black, 1.304200 wide) against whole (1.962000). A padding constant, a frame or a
%% wave amplitude is head-blind and moves both readings together; a half-head-width term
%% moves them apart by exactly half the difference of the two head widths, 0.328900.
%%
%% ⚠️ THE CHORD HAS NO SECONDS, deliberately. A head reversed to the other side of the stem
%% moves the column's own left ink (lily/stem.cc calc_positioning_done), so "how far is the
%% wiggle from the heads" would then be measuring the reversal as well. <c e g> has none, and
%% at c'-e'-g' every head sits below the middle line so the quarter's stem points UP without
%% being told to — its ink stays on the RIGHT, away from the reading.
%%
%% ⚠️ `\fixed c'` is what `lysc ly` emits: Lily#'s absolute `c` is LilyPond's `c'` (HANDOFF 6).
%% ⚠️ `\bar "|."` because LilyPond does not end a score with a final bar line on its own and
%% Lily# always draws one (HANDOFF 6).
%%
%% ⚠️ AR IS THE THIRD BOOK AND IT IS ABOUT THE COLUMN BEFORE. Its second chord is a second in
%% a STEM-DOWN chord, so one head is reversed a full width to the LEFT of the column, and the
%% wiggle has to clear THAT head — which puts the wiggle back where the PREVIOUS chord's ink
%% is. LilyPond's spring lands the previous chord's ink right exactly `padding` from the
%% wiggle, so the reading is the previous head's own width plus that padding, and a spacing
%% that reserved from the un-reversed column left is a whole head width short of it. Lily#
%% drew that collision (the wiggle sat on the previous notehead) until 2026-08-03.
%%
%% ⚠️ ABK/ABW ARE THE SAME CHORD NOT ROLLED — a ChordBracket instead of the wiggle, and the
%% pair AQ-against-ABK is what says which END TREATMENT each grob gets. Both ask the staff for
%% the same head interval, positions = (-3 . -1):
%%
%%   lily/arpeggio.cc:145-151,180-183   the WIGGLE drops its DOWN end half a space and then
%%                                      stacks WHOLE glyphs until they cover what is left.
%%   lily/arpeggio.cc:207-214           the BRACKET widens positions by 0.75 EITHER side and
%%                                      draws one shape — no drop, no quantising.
%%   lily/lookup.cc:542-560 Lookup::bracket   three round_filled_boxes: a spine `thick` wide
%%                                      centred on the grob's origin, and a tick at each end
%%                                      lying INSIDE the Y interval (iv[UP]-thick .. iv[UP])
%%                                      and running from the spine's LEFT edge to `protrusion`
%%                                      past its RIGHT one. So the grob is `thick + protrusion`
%%                                      wide and the ticks cost it no height.
%%
%% ⚠️ `\nonArpeggiato`, NOT `\arpeggioBracket`. They are different grobs: the first makes a
%% ChordBracket (lily/arpeggio-engraver.cc:91-98,140), which is what Lily#'s `@arpeggio(bracket)`
%% means; the second keeps an Arpeggio and re-dresses it. LilyPond's own docstring
%% (ly/property-init.ly:103-104) prefers the first for a non-arpeggiated chord.
%%
%%   AQ   <c e g>4\arpeggio         BLACK heads
%%   AW   <c e g>1\arpeggio         WHOLE heads
%%   AR   <g a>4\arpeggio <b c'>4\arpeggio   the second chord is stem-DOWN, head reversed LEFT
%%   ABK  <c e g>4\nonArpeggiato    AQ's chord as a BRACKET
%%   ABW  <c e g>1\nonArpeggiato    the same bracket over WHOLE heads (head-blindness falsifier)
%%   ABR  <c e g>4 <c e g>4\nonArpeggiato   the room the column BEFORE a bracket is given
%%
%% ⚠️ ABR IS THE READING THE OTHER BRACKET BOOKS CANNOT TAKE, and it is here for the same
%% reason AR is: a grob measured only by its distance from its OWN support cannot show an
%% error that moves the shape and that support together. What it watches is whether the
%% SPACING reserved for the bracket at all — LilyPond adds the ChordBracket to the note
%% column as a conditional item exactly as it adds an Arpeggio
%% (lily/arpeggio-engraver.cc:124-129 acknowledge_note_column, which is blind to which of
%% the three types was made).

%% `what` names the GROB, because two different ones are read here: an Arpeggio (the wiggle)
%% and a ChordBracket (the non-arpeggiated bracket). The reading is the same either way —
%% grob extents on the system — so it is one dump with a label, not two.
#(define ((dump-arpeggio tag what) g)
   (let* ((sys (ly:grob-system g))
          (x (ly:grob-extent g sys X))
          (y (ly:grob-extent g sys Y)))
     (format #t "\nPROBE ~a ~a x=(~,6f . ~,6f) width=~,6f y=(~,6f . ~,6f) length=~,6f\n"
             tag what
             (car x) (cdr x) (- (cdr x) (car x))
             (car y) (cdr y) (- (cdr y) (car y))))
   '())

#(define ((dump-head tag) g)
   (let* ((sys (ly:grob-system g))
          (ext (ly:grob-extent g sys X)))
     (format #t "PROBE ~a HEAD pos=~a x=(~,6f . ~,6f)\n"
             tag
             (ly:grob-property g 'staff-position)
             (car ext) (cdr ext)))
   '())

probe = #(define-music-function (tag) (string?)
           #{ \override Arpeggio.after-line-breaking = #(dump-arpeggio tag "ARPEGGIO")
              \override NoteHead.after-line-breaking = #(dump-head tag) #})

%% The bracket is a DIFFERENT GROB, so it needs its own override — a book with
%% \nonArpeggiato has no Arpeggio in it at all, and `\probe` would print nothing rather
%% than fail, which is the shape a silently empty measurement takes.
probeBracket = #(define-music-function (tag) (string?)
           #{ \override ChordBracket.after-line-breaking = #(dump-arpeggio tag "BRACKET")
              \override NoteHead.after-line-breaking = #(dump-head tag) #})

\paper {
  indent = 0
  ragged-right = ##t
}

\score { \new Staff { \clef treble \time 4/4 \key c \major \probe "AQ"
  \fixed c' { <c e g>4\arpeggio r4 r2 \bar "|." } } }

\score { \new Staff { \clef treble \time 4/4 \key c \major \probe "AW"
  \fixed c' { <c e g>1\arpeggio \bar "|." } } }

\score { \new Staff { \clef treble \time 4/4 \key c \major \probe "AR"
  \fixed c' { <g a>4\arpeggio <b c'>4\arpeggio r2 \bar "|." } } }

\score { \new Staff { \clef treble \time 4/4 \key c \major \probeBracket "ABK"
  \fixed c' { <c e g>4\nonArpeggiato r4 r2 \bar "|." } } }

\score { \new Staff { \clef treble \time 4/4 \key c \major \probeBracket "ABW"
  \fixed c' { <c e g>1\nonArpeggiato \bar "|." } } }

\score { \new Staff { \clef treble \time 4/4 \key c \major \probeBracket "ABR"
  \fixed c' { <c e g>4 <c e g>4\nonArpeggiato r2 \bar "|." } } }

\score { \new Staff { \clef treble \time 4/4 \key c \major \probeBracket "ABT"
  \fixed c' { <c e g>8 <c e g>8\nonArpeggiato r4 r2 \bar "|." } } }
