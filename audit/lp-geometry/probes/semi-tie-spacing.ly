\version "2.26.0"
%% LP FIDELITY PROBE — A HALF-TIE'S SPACING BOX: HOW FAR DOES A LAISSEZ-VIBRER TIE
%% HOLD THE NEXT COLUMN'S ARPEGGIO OFF?
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe semi-tie-spacing.ly -Prefix PROBEX
%%
%% THE QUANTITY THIS ISOLATES (HANDOFF §1 session 361 ⑺⒞①, opened 2026-09-10): the
%% regression book laissez-vibrer-arpeggio (a quarter with an l.v. tie, then an
%% arpeggiated chord, four times) reads 23.25 / 39.00 in Lily# against LilyPond's
%% 23.328 / 39.161 once the wish's skyline stopped holding the half-tie (session 361's
%% second port) — −0.08 a bar, i.e. the two l.v.→arpeggio pairs each 0.04 short. The
%% l.v. tie declares extra-spacing-height (-0.5 . 0.5) (scm/define-grobs.scm:2037),
%% which Lily#'s AddSemiTies notes as NOT ported; the pair here is the ROD from the
%% tie's box (paper column) to the arpeggio (a conditional element of the next column).
%%
%% THE BOOK (twin-exported by `lysc ly` from audit/lp-regression/lys/laissez-vibrer-arpeggio.lys):
%%   LVA  e'4\laissezVibrer <f f'>4\arpeggio e'4\laissezVibrer <g f'>4\arpeggio | … |
%%
%% THE QUANTITY: notehead anchor 0 → anchor 1 (the l.v. quarter's column to the
%% arpeggiated chord's; the chord's two heads share the column X). The dump also
%% prints the LaissezVibrerTie and the Arpeggio extents so the rod can be rebuilt.
%%
%% PREDICTIONS, written before running (RULES 5.0-2):
%%  * LilyPond anchor step 4.244 (the ALLCOL dump of 2026-09-10 read 8.585 → 12.829);
%%    Lily# about 4.20, residual near −0.04. FALSIFIER: a Lily# residual that is not
%%    explained by the tie's Y band (i.e. one that stays after ±0.5 is added to the
%%    band) means the term is the tie's X reach or the arpeggio's box, not the height.
%%  * goes away when: AddSemiTies widens the tie's box by (-0.5 . 0.5) in Y.
%%
%% ★ THE FALSIFIER FIRED ON THE FIRST RUN (2026-09-10): the dump reads the tie's
%% X-extent as (10.0492 . 11.2292) against a head at (8.585 . 9.8892) — head right
%% + 0.16 .. + 1.34, where Lily#'s SemiTieGeometry spans + 0.2 .. + 1.3: the STENCIL is
%% the curve widened by half its line thickness (0.8 × 0.1 / 2 = 0.04) at both ends,
%% and the rod runs tie right + 0.1 + 0.1 + 0.1 + arpeggio 1.3 = 12.8292 exactly, so
%% the 0.04 is the X reach and the Y band never bound here (tie y (-6.61 . -6.24) sits
%% inside the arpeggio's (-9.28 . -5.28) without any widening). The prediction above
%% is kept as written; the port widens X by the half line thickness — and Y by the
%% declared (-0.5 . 0.5), which this book cannot see.
%%
%% ragged-right, indent 0 (from the twin): force 0 = ideals and floors alone.

#(define (probe-dump-pages layout pages)
   (format #t "\nPROBEX PAPER line-width=~a indent=~a\n"
           (ly:output-def-lookup layout 'line-width)
           (ly:output-def-lookup layout 'indent))
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (format #t "PROBEX PAGE ~a systems=~a\n" n (length lines))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let* ((sys (car ls))
                        (sg (ly:prob-property sys 'system-grob)))
                   (if (ly:grob? sg)
                       (let ((all (ly:grob-object sg 'all-elements)))
                         (if (ly:grob-array? all)
                             (for-each
                              (lambda (g)
                                (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                  (if (memq nm '(NoteHead LaissezVibrerTie Arpeggio BarLine))
                                      (format #t "PROBEX GROB ~a ~a name=~a anchor=~a x=(~a . ~a) y=(~a . ~a)\n"
                                              n i nm
                                              (ly:grob-relative-coordinate g sg X)
                                              (+ (ly:grob-relative-coordinate g sg X)
                                                 (car (ly:grob-extent g g X)))
                                              (+ (ly:grob-relative-coordinate g sg X)
                                                 (cdr (ly:grob-extent g g X)))
                                              (car (ly:grob-extent g sg Y))
                                              (cdr (ly:grob-extent g sg Y))))))
                              (ly:grob-array->list all)))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEX BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

lva = \fixed c' {
  \time 4/4
  e4\laissezVibrer <f, f>4\arpeggio e4\laissezVibrer <g, f>4\arpeggio |
  e4\laissezVibrer <a, f>4\arpeggio e4\laissezVibrer <b, f>4\arpeggio |
}

%% LVA — THE L.V. TIE AGAINST THE NEXT ARPEGGIO.
\book {
  \probeTag "LVA"
  \paper { ragged-right = ##t indent = 0 }
  \score { \new Staff { \clef "treble" \lva } }
}
