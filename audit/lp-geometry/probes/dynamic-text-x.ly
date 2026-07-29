\version "2.26.0"
%% LP FIDELITY PROBE — the X model of DynamicText composition (session 36; no ledger
%% points yet — this backs the dynamic-support port's my_dim/X composition and gets its
%% points with that port if any X pair is opened).
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe dynamic-text-x.ly (one book, ~3 s).
%%
%% WHAT IT ANSWERS. The dynamic-support port needs the dynamic label's own profile
%% (my_dim) placed at real letter positions. Three candidate X models disagreed:
%% the generated hmtx advance (f 1.280), the outline bbox width (f 2.156), and the
%% DSQ dump's DynamicText width (f 1.263400). This probe dumps ly:grob-extent about
%% SELF on X and Y for twenty labels — singles pin each letter's X box, pairs/triples
%% pin the letter feed (advance + kern). Identification is by score order.
%%
%% MEASURED (2026-07-29, session 36), against the font's own tables (fontTools over
%% emmentaler-20.otf — LilyPond 2.26.0's own copy and Lily#'s bundled copy are
%% IDENTICAL in advances, kerns and lsb, checked table for table):
%%   * extX left = 0.0 EXACTLY for every label => DynamicText's X-extent is the text
%%     stencil's LOGICAL rect (pen-run frame), not the ink box: Y from ink (the outline
%%     boxes, +2e-5 Pango quantisation), X from the advance run. The lsb overhang
%%     (f -0.408) is NOT in the extent.
%%   * singles (ss): f 1.263302 / p 1.468162 / m 1.741309 / n 1.297446 / r 0.887726 /
%%     s 0.819439 / z 1.126729, against hmtx advances 1.280 / 1.456 / 1.748 / 1.292 /
%%     0.872 / 0.824 / 1.140 — per-glyph deltas BOTH signs, <= 0.0167 ss, not a common
%%     scale (f -1.3%, p +0.8%): Pango's shaping quantisation of the advance run, the
%%     X-side sibling of the Y 2e-5 family. There is NO closed-form recovery; Lily#
%%     computes with the font's numbers and the delta stays a named residual family.
%%   * pairs: pp/ppp/fp/pf/sf/sfz compose with NO kern, additive to 1e-7 (pp = 2p
%%     exact). Kerned feeds, measured vs GPOS: f->f -0.136573 vs -0.152 / m->f
%%     -0.102430 vs -0.116 / m->p +0.239003 vs +0.232 / r->f +0.102430 vs +0.116 /
%%     s->p +0.341433(*) vs +0.348 — every sign and magnitude the font's, the same
%%     quantisation delta on top. GPOS also holds n->f -0.116, n->p +0.232, z->p
%%     +0.232 (unmeasured here). (*) s->p read through the invented "spz" label
%%     jointly with p->z; sfz pins f->z = 0 and GPOS has no p->z, so the joint read
%%     is s->p alone.
%%   ⇒ BAKE the font's advances + GPOS kerns (that IS the computation LilyPond runs,
%%     through Pango); do NOT bake the measured widths (that would paste evaluation
%%     results, HANDOFF 5.2), and do NOT fit the quantisation delta.

#(define labels '("f" "p" "m" "n" "r" "s" "z"
                  "ff" "fff" "pp" "ppp" "mp" "mf" "fp"
                  "sf" "sff" "sfz" "rfz" "spz" "pf"))

#(define (probe-dump-pages layout pages)
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let ((sys (car ls)))
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (eq? nm 'DynamicText)
                                        (format #t "PROBEV DYNX xabs=~a extX=(~a . ~a) extY=(~a . ~a)\n"
                                                (ly:grob-relative-coordinate g sg X)
                                                (car (ly:grob-extent g g X))
                                                (cdr (ly:grob-extent g g X))
                                                (car (ly:grob-extent g g Y))
                                                (cdr (ly:grob-extent g g Y))))))
                                (ly:grob-array->list all))))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEV BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

dynSeq = #(make-sequential-music
           (map (lambda (s)
                  (make-music 'NoteEvent
                              'pitch (ly:make-pitch 0 0 0)
                              'duration (ly:make-duration 2)
                              'articulations
                              (list (make-music 'AbsoluteDynamicEvent 'text s))))
                labels))

\book {
  \probeTag "DXM"
  \paper { ragged-right = ##t indent = 0 }
  \score {
    \new Staff { \dynSeq \bar "|." }
    \layout { }
  }
}
