\version "2.26.0"
%% LP FIDELITY PROBE — THE BOUND VOICE'S SYLLABLE-TO-COLUMN MAP: THE RESERVATION MUST LAND
%% ON THE COLUMN THE SYLLABLE IS DRAWN ON, NOT ON THE PRIMARY VOICE'S ITEM OF THE SAME INDEX.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe lyric-bound-voice-mapping.ly -Prefix PROBEX
%%
%% THE DEFECT THIS MEASURES (HANDOFF 1 small-item "多声小節の byItem 予約写像", named
%% 2026-08-20 when the cross-bar rod port hit it): Lily#'s in-measure lyric reservation
%% (LyricSpacing.ApplyLyricSpacing) maps a syllable to a spring-chain column BY ITEM INDEX
%% whenever the bar's union column count equals the primary voice's item count — but a
%% BOUND (non-primary) voice's ItemIndex counts ITS OWN voice's notes, so on a multi-voice
%% bar whose rhythms differ the reservation lands one-or-more columns LEFT of the column
%% the syllable is DRAWN on (the engraver resolves X from Timing — the correct map; the
%% cross-bar rod edges were moved to the same TIMING map on 2026-08-20, and their doc
%% names this residue: MeasureLineEdges "deliberately NOT the reservations' by-item
%% alias"). LilyPond has no such second map: a LyricSpace spanner rods the two SYLLABLES'
%% ink apart by minimum-distance 0.45 (lily/hyphen-engraver.cc:107,
%% lily/lyric-hyphen.cc:163-179 set_spacing_rods), whichever columns carry them.
%%
%% THE BAR (both voices, all books — the .lys twins' music verbatim, exported by
%% `lysc ly` from scratch/p225; the LYRIC lines are hand-added, the exporter does not
%% export lyrics rows yet):
%%   voice 1 (primary): c''4 c'' c'' c''   — four quarters, columns 0 1 2 3
%%   voice 2 (bound):   e'2  e'4 e'4       — items 0 1 2 on columns 0 2 3
%% Union columns == primary items == 4, so Lily#'s by-item gate passes, and the bound
%% voice's items 1,2 name columns 1,2 where their syllables stand on columns 2,3.
%%
%% THE BOOKS:
%%   LBI  "mumum mumum mumum" under voice 2 — the divergence book. The syllable is wide
%%        enough that the word rod binds BOTH pairs (ink ~9.6 >> natural gaps).
%%   LBIP the same bar, "mumum" x4 under voice 1 — the map-identity control: the primary
%%        voice's ItemIndex IS its column, so Lily#'s two maps coincide. ⚠ THE LP-IDENTITY
%%        PAIR IS LBI vs LBIP: LilyPond's binding rod is ink + 0.45 between CONSECUTIVE
%%        SYLLABLES of one line, so every measured gap in BOTH books must read the same
%%        number D, whichever voice carries the line and whatever column the pair spans.
%%   LBIC the same bar, "u u u" under voice 2 — the no-bind control: ink + 0.45 sits
%%        under every natural gap on both engines, so the lyric terms must leave no trace.
%%        Any Lily# residual HERE is not the mapping (the mis-mapped reservations exist
%%        but cannot bind) — it would be the multi-voice bar's natural spacing itself.
%%
%% THE QUANTITY: syllable ink-centre steps (all syllables of a book are the same word, so
%% centre steps == anchor steps and the face's width cancels; a syllable centres on its
%% aligning extent, so when the rod binds the centre step IS the rod: W + 0.45, column
%% terms cancelled).
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2), mechanism first:
%%  * W = ink("mumum") at the drawn size 2.469417 = 280 Pango px = 9.560111 ss (m u m u m
%%    = 64+44+64+44+64; mu/um carry no kern pair — the LCM 'ru' lesson avoided), so
%%    D = W + 0.45 = 10.010111 ss.
%%  * LBI step0->1 == LBI step1->2 == LBIP steps == D, ONE number across both books
%%    (the LP-identity pair). FALSIFIER: LBI != LBIP means the bar's rhythm or the
%%    carrying voice participates in LilyPond's lyric rod and the identity design is wrong.
%%  * LBIC: no lyric trace — step1->2 = the bar's natural quarter column gap (~3.0);
%%    step0->1 = the natural col0->col2 span (~6.1) minus the half-vs-quarter alignment
%%    sliver (he.centre 0.6887 vs 0.6521 = 0.0366).
%%  * Which side diverges (HANDOFF 5.0): Lily# reads LBIP and LBIC exact (the map is
%%    identity / the reservation cannot bind) and forks LBI BOTH WAYS AT ONCE: step0->1
%%    ~ 2D (each mis-mapped reservation lands one spring left, so BOTH pile into the
%%    col0->col2 span: predicted +D over LilyPond) and step1->2 ~ natural 3.0 (nothing
%%    reserves the pair that needed D: predicted -(D - 3.0), the two inks overlapping by
%%    ~6.6 — the full-size face of named-voice-lyrics' 0.06 'deep'-on-'slow' overlap).
%%    Lily#'s own SVG (scratch/p225, F2-quantized) already shows the shape: centres
%%    11.73 / 31.79 / 34.79 = steps 20.06 / 3.00 against D 10.01.
%%
%% ⚠️ `\\` names its voices "1" and "2" (LP creates implicit Voice contexts with those
%% names), which is what \lyricsto binds to — the twin spells the bar with \\, so the
%% probe does too rather than restructuring into \new Voice.
%%
%% ⚠️ indent = 0 comes from the twin itself. ragged-right so force 0 = the rods and
%% ideals alone decide every X (the regime every note-to-note point in the ledger uses).
%%
%% NOTE: inside #(...) the comment character is `;`, not `%%`.

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
                                  (if (memq nm '(NoteHead LyricText BarLine))
                                      (format #t "PROBEX GROB ~a ~a name=~a anchor=~a x=(~a . ~a) text=~a\n"
                                              n i nm
                                              (ly:grob-relative-coordinate g sg X)
                                              (+ (ly:grob-relative-coordinate g sg X)
                                                 (car (ly:grob-extent g g X)))
                                              (+ (ly:grob-relative-coordinate g sg X)
                                                 (cdr (ly:grob-extent g g X)))
                                              (if (eq? nm 'LyricText)
                                                  (ly:grob-property g 'text)
                                                  "")))))
                              (ly:grob-array->list all)))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

%% Both text faces pinned (lyric-column-spacing.ly's reason): the syllables ARE the
%% binding ink here, so the face must be the one Lily# measures with.
probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEX BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

m = \fixed c' {
  \time 4/4
  \key c \major
  << { c'4 c' c' c' | } \\ { e2 e4 e4 | } >>
}

%% LBI — THE DIVERGENCE BOOK: wide words on the bound voice.
\book {
  \probeTag "LBI"
  \paper { ragged-right = ##t indent = 0 }
  \score {
    <<
      \new Staff { \clef "treble" \m }
      \new Lyrics \lyricsto "2" { mumum mumum mumum }
    >>
  }
}

%% LBIP — THE MAP-IDENTITY CONTROL: the same words on the primary voice.
\book {
  \probeTag "LBIP"
  \paper { ragged-right = ##t indent = 0 }
  \score {
    <<
      \new Staff { \clef "treble" \m }
      \new Lyrics \lyricsto "1" { mumum mumum mumum mumum }
    >>
  }
}

%% LBIC — THE NO-BIND CONTROL: syllables too narrow to bind on either engine.
\book {
  \probeTag "LBIC"
  \paper { ragged-right = ##t indent = 0 }
  \score {
    <<
      \new Staff { \clef "treble" \m }
      \new Lyrics \lyricsto "2" { u u u }
    >>
  }
}
