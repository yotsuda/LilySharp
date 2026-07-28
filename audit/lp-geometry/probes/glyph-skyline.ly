\version "2.26.0"
%% LP FIDELITY PROBE — a glyph's SKYLINE against its own stencil extent.
%%
%% Why this exists. The vertical ledger's last four residuals are all one number: Lily#
%% gives the clef the extent its LILC bbox states (3.550 below the staff refpoint) while
%% the value LilyPond's springs are floored by works back to 3.540-3.545. LilyPond's own
%% STENCIL extent agrees with the bbox, so the difference is not the metric — it is in how
%% a skyline is built from a glyph. This probe asks the grob for both at once.
%%
%% ANSWERED 2026-07-28, and the answer is that a glyph's SKYLINE is not its EXTENT:
%%
%%   CLEF-G       ext=(-2.550 . 4.800)   skyline=(-2.540 . 4.776)
%%   NOTEHEAD     ext=(-0.545 . 0.545)   skyline=(-0.545 . 0.545)
%%   STAFFSYMBOL  ext=(-2.050 . 2.050)   skyline=(-2.050 . 2.050)
%%
%% The extent is the declared (LILC) bbox and Lily# reads the same numbers for it. The
%% skyline is built from the glyph's OUTLINE instead:
%% lily/stencil-integral.cc:535-563 add_named_glyph_segments takes the declared bbox,
%% takes the outline bbox from FreeType (get_glyph_outline_bbox), and scales the OUTLINE by
%% `bbox[X].length () / real_bbox[X].length ()` — a ratio of WIDTHS — before adding it to
%% the skyline. So the vertical numbers are the outline's own, carried in on the width's
%% scale, and they coincide with the declared box only for a glyph that fills it: a
%% notehead does, the G clef does not (0.024 of slack above, 0.010 below).
%%
%% ⇒ And that ratio is 1 for a CFF glyph, which is what closes the question: LilyPond's own
%% comment at :549-550 says the bbox is "how it would be calculated ... if it were based on
%% real extents", and get_unscaled_indexed_char_dimensions does agree with the outline, so
%% what is left is the plain unit conversion. Measured both ways: clefs.G's outline is
%% (2 . -635) (645 . 1194) in font units (fontTools BoundsPen, and freetype.cc:68
%% ly_FT_get_glyph_outline_bbox loads FT_LOAD_NO_SCALE and calls FT_Outline_Get_BBox, so
%% LilyPond reads the same units), and 635 x 0.004 = 2.540 with 1194 x 0.004 = 4.776 — the
%% dump above to six digits.
%%
%% ⚠️ THE 0.27% HANDOFF 2C CARRIED FOR SESSIONS AS "unexplained, needs instrumenting" WAS A
%% UNIT MIX-UP: 2.565 / 643 divides a STAFF-SPACE width by a FONT-UNIT one. The scale is
%% 0.004, the same conversion the generator already applies to the LILC numbers.
%% ⇒ Nothing needs instrumenting and nothing needs SKPath: the generator (fontTools) can
%% emit outline x 0.004 as a SECOND box, and the skyline seeds that while the extent stays
%% LILC — the same two boxes LilyPond keeps.
%%
%% ⚠️ WHAT IT COSTS TO PORT: this changes what every clef reserves on every book, so it is
%% not a local fix — see HANDOFF 2C. The numbers above are LilyPond's and are the target;
%% adjusting a bbox to land on them is section 5.2.
%%
%% `vertical-skylines` is a Skyline_pair, which reaches Scheme as a plain CONS of two
%% skyline smobs (lily/lily-guile.cc:503-506 — car is the LEFT/DOWN one, cdr the
%% RIGHT/UP one; scm/c++.scm:242-245 is the predicate). ly:skyline-max-height returns the
%% INTERNAL height, i.e. sky * coordinate, so a DOWN skyline reports a POSITIVE number for
%% ink hanging below its reference point.
%%
%% Everything is in staff spaces. Y-offset places the grob against the staff, so the ink
%% below the staff's own refpoint is  -(Y-offset) + (that positive down height)  read with
%% the sign the dump prints.

#(define (probe-glyph name)
   (lambda (grob)
     (let* ((sky (ly:grob-property grob 'vertical-skylines))
            (ext (ly:grob-extent grob grob Y))
            (yoff (ly:grob-property grob 'Y-offset 0)))
       (format #t "\nPROBEG ~a yoff=~a ext=(~a . ~a) skyline-down=~a skyline-up=~a\n"
               name
               yoff
               (car ext) (cdr ext)
               (if (pair? sky) (ly:skyline-max-height (car sky)) 'NONE)
               (if (pair? sky) (ly:skyline-max-height (cdr sky)) 'NONE)))))

%% One bar, one clef. The music is irrelevant; only the Clef grob is interrogated.
\book {
  \score {
    \new Staff \with {
      \override Clef.after-line-breaking = #(probe-glyph "CLEF-G")
      \override NoteHead.after-line-breaking = #(probe-glyph "NOTEHEAD")
      \override StaffSymbol.after-line-breaking = #(probe-glyph "STAFFSYMBOL")
    }
    { c'1 }
  }
}

%% THE DYNAMIC, on exactly the music of book D in page-vertical.ly. That entry measures the
%% staff-to-staff distance and lands at residual +1.866924, which decomposes into TWO
%% divergences of opposite sign — a phantom stem worth +2.955 and the dynamic's own vertical
%% footprint worth -1.088076 (Lily# spends 2.100000 below the note's claimed bottom,
%% LilyPond 3.188076). That second number was INFERRED by subtracting the first from the
%% total; this book measures it instead.
%%
%% Both grobs are asked because the footprint is split between them: DynamicLineSpanner
%% carries the padding that holds the pair off the staff (scm/define-grobs.scm:1408
%% (padding . 0.6)) and DynamicText carries the glyph's own ink. Lily# had THREE different
%% descents for that glyph, none traced to a LilyPond line, and this book was written to
%% decide between them.
%%
%% ANSWERED: none of them. DynamicText reports ext (-0.692002 . 1.896021) — the `f`
%% GLYPH's own ink, so the quantity is per-dynamic and no constant can be right (p
%% descends, m does not). Lily# now reads it from the font, from the OUTLINE rather than
%% from LILC because DynamicText is text (font-encoding fetaText + ly:text-interface::print
%% -> Modified_font_metric::text_stencil -> Pango over the outline), which is the opposite
%% source from ec7a2254 for exactly the reason ec7a2254 chose LILC. The entry closed to
%% -0.000076, that remainder being Pango's quantisation of the outline.
%%
%% Y-offset is printed for both, because the footprint below the note is
%% -(Y-offset) + the down skyline's height, in the staff's own frame.
\book {
  \score {
    \new Staff \with {
      \override DynamicText.after-line-breaking = #(probe-glyph "DYNAMIC-TEXT")
      \override DynamicLineSpanner.after-line-breaking = #(probe-glyph "DYNAMIC-SPANNER")
      \override NoteHead.after-line-breaking = #(probe-glyph "NOTEHEAD-A")
    }
    << { \voiceOne b'1 } \\ { \voiceTwo a1\f } >>
  }
}
