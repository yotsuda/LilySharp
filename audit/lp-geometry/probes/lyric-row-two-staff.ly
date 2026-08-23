\version "2.26.0"
%% LP FIDELITY PROBE — AN INDEPENDENT LYRICS ROW STANDING BELOW *TWO* STAVES.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe lyric-row-two-staff.ly -Prefix PROBER
%%
%% THE CELL THE LEDGER DOES NOT HAVE. The table already carries
%% `lyrics.row.staff-to-lyric` = 5.5 (a row under ONE staff) and
%% `lyrics.two-staff.staff-to-lyric` = 5.5 (TWO staves, lyrics note-bound), but nothing where
%% a row stands below TWO staves — and Lily#'s corpus never spells it either (census, session
%% 238: 575 score blocks, 36 with two or more staves, none of those with a lyrics row). That
%% is the shape a user reported syllables engraved THROUGH the lower staff's noteheads on
%% every system but the last.
%%
%% THE QUESTION, and it is not "where does the row go" but WHICH TERM BINDS. LilyPond's
%% Align_interface::internal_get_minimum_translations sets dy = down_skyline.distance(up) +
%% padding and then raises it to `minimum-distance` (align-interface.cc:228-233); the
%% spring's `basic-distance` 5.5 is the IDEAL and arrives later, in distribute_loose_lines.
%% So 5.5 is what a row reads when the staff's ink stops at its bottom line and the skyline
%% term is the smaller one. THE OPEN QUESTION IS WHAT HAPPENS WHEN IT IS NOT: with notes
%% hanging below the staff, does the skyline push the row past 5.5, or is 5.5 a floor the
%% ink cannot move?
%%
%% WHY IT MATTERS TO THE PORT. Before session 238's fix, Lily# read 5.5 here on any system
%% with slack and LESS on any system without — because the per-staff skyline was never
%% wired for a row-only score, so the chain's first gap carried a NEGATIVE minimum and
%% nothing floored it. 5.5 was arrived at as the spring's natural length, not as a floor.
%% If LilyPond reads 5.5 on RD below, the old number was right for the wrong reason and the
%% fix over-corrects; if it reads more, the fix is right and the old agreement on the last
%% system was a coincidence of slack.
%%
%% THE PAIR (HANDOFF 5.0-1): RD and RF are ONE VARIABLE apart — the LOWER staff's pitch, and
%% nothing else. Same two staves, same two verses, same three systems, same syllables, same
%% paper. RD hangs c'1 one ledger line BELOW the treble staff; RF puts b'1 on the middle
%% line, where the staff's own ink is the whole extent. Everything that could differ between
%% the books other than the skyline term is held fixed by construction.
%%
%% THREE SYSTEMS ON PURPOSE, all with identical content: the reported defect was invisible
%% on the LAST system of every book (its chain runs to the page edge, where the room is
%% unbounded and a missing floor never binds). A pair of one-system books cannot see it, and
%% a two-system book sees it once. With three, "the same on every system" is a claim with
%% two independent witnesses.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2), mechanism first:
%%  * RF — 5.5 exactly, on all three systems. The staff's ink ends at its bottom line 2.0
%%    below the refpoint, the syllable's ascender is under 1.5, so the skyline term is
%%    around 3.5-4 and the 5.5 ideal is what the solve settles on. This is the control, and
%%    it should reproduce the ledger's existing `lyrics.row.staff-to-lyric` = 5.5.
%%    FALSIFIER: anything but 5.5 means this book is not the arrangement that point measures
%%    and the pair is not anchored.
%%  * RD — MORE than 5.5, by about the depth the notes add. The c' head centre sits 3.0
%%    below the refpoint, its ink bottom about 3.5, plus the ledger line, plus padding, plus
%%    the syllable's own rise: order 6.5-7.5. FALSIFIER, AND IT IS THE WHOLE PROBE: a
%%    reading of 5.5 on RD means the ink does NOT push the row, the skyline term never binds
%%    over the ideal for a Lyrics context, and session 238's fix moved Lily# AWAY from
%%    LilyPond — in which case the defect is real but the repair is wrong, and what needs
%%    fixing is only the per-system fork.
%%  * THE FORK RD − RF (predicted 1-2 ss) is the mechanism itself: if the two books read the
%%    same number, the lower staff's ink is not in the distance at all.
%%  * BOTH BOOKS, EVERY SYSTEM: one number per book. A per-system fork inside either book
%%    would mean LilyPond lets the inter-system room compress the block, which is what
%%    Lily# did before the fix — and would make the pre-fix behaviour the faithful one.
%%  * The anchor is the LOWER staff, not the sung upper one: verse 1 stands below the lower
%%    staff's refpoint, not between the two. FALSIFIER: a baseline between the staves means
%%    LilyPond anchors a \lyricsto row on the staff it SINGS rather than on the last
%%    spaceable line above it, and Lily#'s whole "block below the system" model is wrong for
%%    this shape rather than merely mis-floored.
%%  * All books: 1 page, 3 systems, 2 staves each. A book that wraps differently is out of
%%    its regime and its numbers are not comparable.
%%
%% MEASURED — THREE PREDICTIONS OUT OF SIX MISSED, and the misses are the useful half.
%%
%%   book  system 1    system 2    system 3 (last)
%%   RD    5.226460    5.226460    5.500001
%%   RF    3.772457    3.772457    5.500001
%%
%%  * MISS, and it is the one that reframes the whole island: BOTH BOOKS FORK BY SYSTEM.
%%    The prediction said one number per book. What LilyPond does is solve the two systems
%%    that have another one after them into a BOUNDED room, where the chain lands on its
%%    MINIMUM, and let the last system's chain run to the page edge (:1004-1013), where the
%%    room is unbounded and the line relaxes to the 5.5 basic-distance IDEAL. The two bounded
%%    systems agree to the last digit in both books, so this is "bounded vs unbounded" and
%%    not a gradient — and a fork by system is therefore FAITHFUL, not the defect it looked
%%    like from the Lily# side.
%%  * MISS: RF was predicted 5.5 on all three systems and reads 3.772457 on the bounded ones.
%%    The 5.5 is an ideal, and an ideal only survives where nothing squeezes it.
%%  * MISS: RD was predicted to be pushed PAST 5.5, order 6.5-7.5. It reads 5.226460 — LESS
%%    than the ideal. The ink term is a FLOOR, not a push: it decides where the line may not
%%    go above, and it is visible only where the room is tight enough to press the line into
%%    it. On this book the floor sits just under the ideal, which is why the last system can
%%    still relax past it.
%%    ⚠️ THE FALSIFIER THIS ALMOST TRIPPED. The header said "a reading of 5.5 on RD means the
%%    ink does not push the row and session 238's fix moved Lily# AWAY from LilyPond". RD's
%%    LAST system does read 5.5 — so a probe that measured one system would have fired that
%%    falsifier and reverted a correct fix. What saves it is the bounded systems reading
%%    5.226460 against RF's 3.772457 on the same paper: the ink is in the distance, it is
%%    just not always the binding term. ⇒ A FALSIFIER HAS TO NAME THE REGIME IT APPLIES IN.
%%  * HIT: the fork RD - RF = 1.454003, inside the predicted 1-2.
%%  * HIT: the anchor is the LOWER staff — verse 1 stands below it on every system, never
%%    between the two staves.
%%  * HIT: 1 page, 3 systems, 2 staves each, both books.
%%
%% WHAT THE PORT READS AGAINST THIS (session 238, entries lyrics.row.two-staff.*):
%% Lily# after the fix reproduces all six cells — bounded systems, last systems, and the fork
%% — with residuals +0.040901 (RD bounded), -0.000096 (RF bounded) and -0.0000009 (both last
%% systems). BEFORE the fix it read 2.970000 / 3.080000 / 5.500000 on RD: two staff spaces
%% high on both bounded systems, and EXACT on the last, because with no floor under it the
%% spring simply stopped at its natural length. ⇒ The pre-fix agreement on the last system
%% was the same number for a different reason, which is what the mid-system entry exists to
%% keep anyone from mistaking for coverage.
%%
%% ⚠️ indent = 0 is load bearing (system-clef-floor.ly's reason): append_system shifts each
%% system's skylines by its own indent before measuring.
%%
%% ⚠️ Both text faces are pinned (pedal-lyric-stack.ly's reason): the syllable IS ink in the
%% binding term here, so the face has to be the one the twin is measured with.
%%
%% NOTE: inside #(...) the comment character is `;`, not `%%`.

#(define (probe-dump-pages layout pages)
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (format #t "PROBER PAGE ~a systems=~a\n" n (length lines))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let* ((sys (car ls))
                        (sg (ly:prob-property sys 'system-grob)))
                   (if (ly:grob? sg)
                       (begin
                         ;; The alignment's own elements, in order, with staff-affinity: this
                         ;; is what says WHICH line is the last spaceable one and therefore
                         ;; what the row hangs from. rel is the element's reference point.
                         (let ((align (ly:grob-object sg 'vertical-alignment)))
                           (if (ly:grob? align)
                               (let ((k 0))
                                 (for-each
                                  (lambda (g)
                                    (format #t "PROBER VAG sys=~a el=~a rel=~a aff=~a ext=(~a . ~a)\n"
                                            i k
                                            (ly:grob-relative-coordinate g sg Y)
                                            (ly:grob-property g 'staff-affinity)
                                            (car (ly:grob-extent g g Y))
                                            (cdr (ly:grob-extent g g Y)))
                                    (set! k (1+ k)))
                                  (ly:grob-array->list (ly:grob-object align 'elements))))))
                         ;; LyricText carries the row's baseline; NoteHead and LedgerLineSpanner
                         ;; carry the lower staff's ink, which is the term under test.
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (memq nm '(LyricText NoteHead))
                                        (format #t "PROBER GROB sys=~a name=~a rel=~a ext=(~a . ~a) x=~a\n"
                                                i nm
                                                (ly:grob-relative-coordinate g sg Y)
                                                (car (ly:grob-extent g g Y))
                                                (cdr (ly:grob-extent g g Y))
                                                (ly:grob-relative-coordinate g sg X)))))
                                (ly:grob-array->list all))))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBER BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

%% The sung staff, identical on every system: notes inside the staff, so the UPPER staff
%% contributes nothing to the distance under test.
upSys  = { e'2 e'2 | e'2 e'2 | }
verseA = \lyricmode { gyp gyp gyp gyp }
verseB = \lyricmode { pug pug pug pug }

%% RD — THE LOWER STAFF'S INK HANGS BELOW IT. c'1 is one ledger line under the treble staff,
%% which is the reported book's shape.
\book {
  \probeTag "RD"
  \paper { ragged-bottom = ##t  indent = 0 }
  \score {
    <<
      \new Staff { \new Voice = "mel" { \time 4/4
        \upSys \break \upSys \break \upSys } }
      \new Staff { \time 4/4 c'1 | c'1 | c'1 | c'1 | c'1 | c'1 | }
      \new Lyrics \lyricsto "mel" { \verseA \verseA \verseA }
      \new Lyrics \lyricsto "mel" { \verseB \verseB \verseB }
    >>
  }
}

%% RF — THE SAME BOOK WITH THE LOWER STAFF'S INK INSIDE IT. b'1 is the middle line. One
%% variable apart from RD.
\book {
  \probeTag "RF"
  \paper { ragged-bottom = ##t  indent = 0 }
  \score {
    <<
      \new Staff { \new Voice = "mel" { \time 4/4
        \upSys \break \upSys \break \upSys } }
      \new Staff { \time 4/4 b'1 | b'1 | b'1 | b'1 | b'1 | b'1 | }
      \new Lyrics \lyricsto "mel" { \verseA \verseA \verseA }
      \new Lyrics \lyricsto "mel" { \verseB \verseB \verseB }
    >>
  }
}
