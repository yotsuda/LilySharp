\version "2.24.4"
%% LP FIDELITY PROBE — the accidental horizontal-skylines and column packing that
%% Extract-EmmentalerSkylines.py -> GlyphSkylinesGenerated.cs reproduce, plus the
%% note-to-accidental and column gaps position_apes produces.
%%
%% WHY. Island 2 (commit e08f5e12) bakes each accidental's REAL glyph outline skyline and
%% packs a chord's accidentals with LilyPond's position_apes (accidental-placement.cc:412,
%% ape->horizontal_skylines_[RIGHT].distance(left_skyline, 0.1)). The extractor was verified
%% against a LIVE dump of these skylines; this probe IS that dump, committed so the 15-digit
%% match stays re-runnable instead of evaporating into a comment.
%%
%% Run (Guile deadlocks on an inherited console, so detach with < NUL):
%%   cmd /c "lilypond -dbackend=null -o out accidental-skyline.ly > out.txt 2> err.txt < NUL"
%% Exit code 1 is normal under -dbackend=null; the dump on stdout is complete.
%%
%% `horizontal-skylines` is a Skyline_pair reaching Scheme as a CONS (car = LEFT, cdr =
%% RIGHT). ly:skyline->points draws the outline; everything is in staff spaces.

#(define (dump-sky name)
   (lambda (grob)
     (let* ((skyp (ly:grob-property grob 'horizontal-skylines))
            (extx (ly:grob-extent grob grob X))
            (exty (ly:grob-extent grob grob Y)))
       (if (pair? skyp)
           (begin
             (format #t "\nSKY ~a extX=(~a . ~a) extY=(~a . ~a)\n"
                     name (car extx) (cdr extx) (car exty) (cdr exty))
             (format #t "SKY ~a LEFT ~s\n" name (ly:skyline->points (car skyp) Y))
             (format #t "SKY ~a RIGHT ~s\n" name (ly:skyline->points (cdr skyp) Y)))
           (format #t "\nSKY ~a NONE\n" name)))
     '()))

#(define (rec name)
   (lambda (grob)
     (let* ((sys (ly:grob-system grob))
            (x (ly:grob-relative-coordinate grob sys X))
            (ext (ly:grob-extent grob grob X)))
       (format #t "\nGAP ~a x=~a extL=~a extR=~a\n" name x (car ext) (cdr ext)))
     '()))

%% (1) The lone flat / sharp horizontal-skyline pairs. EXPECTED (LilyPond 2.26.0), which the
%%     extractor reproduces exactly (scale is 1.0 for accidentals = raw outline / 250 ss):
%%       flat  LEFT bottoms at x=-0.108 (the OUTLINE left, NOT the -0.12 grob extent);
%%             RIGHT floors at x=0.30 = 0.80*0.375 over the LILC Y-extent [-0.63,1.83]
%%             (accidental.cc:75-81), bowl peaking at x=0.80.
%%       sharp RIGHT notches to x=0.864 between the # bars, poking to x=1.10 at them.
\book { \score { \new Staff \with {
  \override Accidental.after-line-breaking = #(dump-sky "FLAT")
} { bes'1 } } }
\book { \score { \new Staff \with {
  \override Accidental.after-line-breaking = #(dump-sky "SHARP")
} { fis'1 } } }

%% (2) The gaps position_apes produces, read off the drawn output. EXPECTED:
%%       single sharp / flat: ink-right sits 0.35 left of the note-head left edge
%%         (right-padding 0.15 + padding 0.20).
%%       two sharps a third apart <fis' ais'>: columns 1.284000 apart.
%%       two flats  a third apart <bes' des''>: columns 0.964561 apart.
%%     (The column gaps are also the committed ledger points
%%      chord.accidental.{sharp,flat}-column-gap-*; these two scores reproduce them directly.)
\book { \score { \new Staff \with {
  \override NoteHead.after-line-breaking = #(rec "SHARP1-HEAD")
  \override Accidental.after-line-breaking = #(rec "SHARP1-ACC")
} { fis'1 } } }
\book { \score { \new Staff \with {
  \override NoteHead.after-line-breaking = #(rec "FLAT1-HEAD")
  \override Accidental.after-line-breaking = #(rec "FLAT1-ACC")
} { bes'1 } } }
\book { \score { \new Staff \with {
  \override NoteHead.after-line-breaking = #(rec "CSHARP-HEAD")
  \override Accidental.after-line-breaking = #(rec "CSHARP-ACC")
} { <fis' ais'>1 } } }
\book { \score { \new Staff \with {
  \override NoteHead.after-line-breaking = #(rec "CFLAT-HEAD")
  \override Accidental.after-line-breaking = #(rec "CFLAT-ACC")
} { <bes' des''>1 } } }
