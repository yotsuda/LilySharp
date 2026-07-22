\version "2.24.4"
%% LP FIDELITY PROBE — a glyph's SKYLINE against its own stencil extent.
%%
%% Why this exists. The vertical ledger's last four residuals are all one number: Lily#
%% gives the clef the extent its LILC bbox states (3.550 below the staff refpoint) while
%% the value LilyPond's springs are floored by works back to 3.540-3.545. LilyPond's own
%% STENCIL extent agrees with the bbox, so the difference is not the metric — it is in how
%% a skyline is built from a glyph. This probe asks the grob for both at once.
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
