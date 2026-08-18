\version "2.26.0"
%% LP FIDELITY PROBE — WHAT EM AND WHAT FACE a plain-text music mark is set in. The book
%% HANDOFF §1 ⑷ ⒞ asks for by name: MusicMarkEngraver.PlainTextFontSize is 2.8 ss and
%% TextStyleOf draws BoldItalic, and the LILYSHARP-OWN tag beside both says neither is
%% LilyPond's — but nothing had MEASURED either, so the tag names a suspicion and the
%% ledger holds no point. This file prices both against real LilyPond, in one run.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe jump-mark-em.ly (ten tiny books, seconds).
%%
%% WHAT IS BEING MEASURED
%%
%% LILYPOND-REF: scm/define-grobs.scm:1898-1926 JumpScript — (font-shape . italic),
%%   (stencil . ly:text-interface::print), and NO font-series and NO font-size. A grob that
%%   declares no font-size is set at the paper's own text-font-size.
%% LILYPOND-REF: scm/paper.scm:68-88 layout-set-absolute-staff-size-in-module —
%%   text-font-size = 11 * factor with factor = staff-height / (20 pt), and staff-space =
%%   staff-height / 4. At the default 20 pt staff that is 11 pt over a 5 pt staff space:
%%   EM = 2.2 ss, which is the number EngravingDefaults.TextScriptFontSize already carries.
%% LILYPOND-REF: scm/define-grobs.scm:3190-3208 SostenutoPedal and :4148-4166 UnaCordaPedal
%%   — (font-shape . italic), (stencil . ly:text-interface::print), again no series, no size.
%% LILYPOND-REF: lily/sustain-pedal.cc:47-76 Sustain_pedal::print — the sustain pedal is NOT
%%   TEXT AT ALL. It walks the string and pastes MUSIC-FONT glyphs (pedal.Ped, pedal.., ...)
%%   edge to edge with zero padding; the file's own comment says "we have no kerning" and
%%   "FIXME. Need to use markup." Lily# draws "Ped." as an upright BOLD SERIF STRING
%%   (MusicMarkEngraver.TextStyleOf returns Bold for SustainOn/SustainOff alone), so this
%%   pair does not compare two sizes of one mechanism — it compares two mechanisms.
%% LILYPOND-REF: lily/pango-font.cc:351-362 Pango_font::pango_item_string_stencil — a text
%%   grob's X extent is the LOGICAL rectangle, i.e. the shaped ADVANCE. Established for this
%%   corpus by text-advance.ly, whose ladder ("nn" exactly 2x "n") proved it from outside.
%%
%% WHY THE EM IS READABLE FROM A WIDTH. text-advance.ly measured that every LilyPond text
%% width is an integer number of q = 0.034143307086614 ss — ONE device pixel at
%% PANGO_RESOLUTION 1200 — and that each glyph's advance is round(advance_em x ppem) with
%% ppem = em_ss x 1.757299018 mm/ss x 1200/72 px/mm. ppem is 64.434297 px at em 2.2 and
%% 82.007288 px at em 2.8, so the two hypotheses are 27% apart on every string: no fit, no
%% subtraction, no shared unknown. AND THE STENCIL SAYS IT OUTRIGHT — a text stencil's
%% expression is (glyph-string <font> <em-in-mm> ...), which text-advance.ly read as
%% "C059-Italic 3.865234375" = 2.2 ss x 1.757299018 mm/ss. This probe prints that expression
%% for every grob it measures: ASK THE STENCIL WHAT IT DREW, rather than deriving the face
%% and the size from a width that carries both.
%%
%% THE BOOKS come in pairs by construction — a JumpScript beside a TextScript carrying the
%% SAME STRING — so the mark's em and face are read as a DIFFERENCE against a text whose em
%% and face this corpus has already pinned to nine digits.
%%
%%   JMF  \fine                    JumpScript "Fine"
%%   TIF  ^\markup \italic         TextScript "Fine"          <- em 2.2, italic
%%   TBF  ^\markup \bold \italic   TextScript "Fine"          <- em 2.2, BOLD italic
%%   JMJ  \jump "D.S. al Coda"     JumpScript, a long string (spaces and periods)
%%   TIJ / TBJ                     the same two controls for it
%%   PSO  \sostenutoOn             SostenutoPedal "Sost. Ped."
%%   TIS  ^\markup \italic         TextScript "Sost. Ped."    <- its control
%%   PSU  \sustainOn               SustainPedal — THE GLYPH MECHANISM
%%   TBP  ^\markup \bold           TextScript "Ped." UPRIGHT BOLD <- what Lily# draws instead
%%
%% PREDICTIONS, written before the run (HANDOFF §5.0-2, signs included):
%%
%%   * JMF == TIF EXACTLY, to LilyPond's own printed digits, and both stencils name the
%%     same face at the same mm. That is the whole claim of the two LILYPOND-REFs above
%%     restated as an observable. FALSIFIER, and it is a real one: if JMF is WIDER than TIF
%%     by about 27%, the JumpScript is NOT set at the paper's text-font-size and the
%%     LILYSHARP-OWN tag's premise is wrong — 2.8 would then be somebody's port of a size
%%     this probe cannot see, not an invention.
%%   * TBF > TIF. C059-Bold-Italic's advances are wider than C059-Italic's; sign certain,
%%     size unpredicted (it is a table, not a scalar — HANDOFF §5 on "font quantity" labels).
%%   * JMF's stencil says em 3.8660578 mm (2.2 ss). If it says 4.9204373 mm (2.8 ss) the
%%     prediction above has already failed and the rest of the file is void.
%%   * PSO == TIS, for the identical reason as the JumpScript pair (same three declarations).
%%   * PSU is NOT a multiple of q at all, or if it is, it is one by accident: it is a sum of
%%     Emmentaler glyph widths, which live on the music font's grid and not on Pango's.
%%     ⚠️ This is the prediction that says the sustain pedal is a DIFFERENT ISLAND from the
%%     other two, and it must be checked before any residual there is called a size error.
%%   * Lily# is WIDER than LilyPond on every mark here, by the em ratio 2.8/2.2 = 1.2727
%%     compounded with the bold term — order +25..+35% of the LilyPond width. Sign certain
%%     for the marks; the sustain pedal's sign is NOT predicted, because a bold "Ped." in a
%%     serif face and three Emmentaler glyphs have no arithmetic in common.
%%
%% MEASURED 2026-08-18 (this file, two runs — the second added TBS, see its book):
%%
%%     tag  grob            string          ss                    px
%%     JMF  JumpScript      "Fine"          4.506916535433071    132
%%     TIF  TextScript i    "Fine"          4.506916535433071    132
%%     TBF  TextScript bi   "Fine"          5.019066141732285    147
%%     JMJ  JumpScript      "D.S. al Coda" 12.906170078740160    378
%%     TIJ  TextScript i    "D.S. al Coda" 12.906170078740157    378
%%     TBJ  TextScript bi   "D.S. al Coda" 13.964612598425195    409
%%     PSO  SostenutoPedal  "Sost. Ped."    9.901559055118110    290
%%     TIS  TextScript i    "Sost. Ped."    9.901559055118110    290
%%     TBS  TextScript bi   "Sost. Ped."   10.686855118110234    313
%%     PSU  SustainPedal    "Ped."          3.472000000000001    101.69  <- NOT an integer
%%     PSU  SustainPedal    "*"             1.555599999999998     45.56  <- NOT an integer
%%     TBP  TextScript b    "Ped."          4.950779527559053    145
%%
%%   ⑴ EVERY PREDICTION HELD. JMF == TIF and PSO == TIS to fifteen digits; TBF > TIF and
%%     TBJ > TIJ; every text stencil names "C059-Italic 3.865234375" (or its bold/upright
%%     sibling), i.e. em 2.2 ss x 1.757299018 mm/ss = 3.8660578 mm rounded to LilyPond's own
%%     printed precision; and the two SustainPedal readings are the only ones in the table
%%     that are not whole pixels. ⇒ THE MARK'S EM IS 2.2 AND ITS FACE IS ITALIC, from the
%%     drawing's own account of itself and not from a width that carries both.
%%   ⑵ THE WEIGHT IS A TABLE, NOT A SCALAR — which is what forbids reusing one string's
%%     bold term for another's, and is why TBS exists: TBF/TIF is 1.1136, TBJ/TIJ is 1.0820,
%%     TBS/TIS is 1.0793. Three strings, three ratios.
%%   ⑶ THE SUSTAIN PEDAL IS A DIFFERENT ISLAND, as predicted and now with the stencil to
%%     prove it: (combine-stencil (translate-stencil (3.192 . 0.0) (named-glyph … "pedal.."))
%%     (named-glyph … "pedal.Ped")) — two Emmentaler glyphs at 0.5690551181102361, no face,
%%     no kerning, and 3.192 of the 3.472 is the first glyph's own advance. For scale,
%%     LilyPond's own bold serif "Ped." at the paper size (TBP) is 1.43x wider than the
%%     glyph string it does not use.
%%   ⚠️ ⑷ THE PREDICTION THAT WAS WRONG, kept because it is the one that bought something:
%%     "Lily# is wider by order +25..+35%". It is +43% on Fine, +39% on both long strings
%%     and +82% on the sustain pedal. The under-estimate came from adding the size and
%%     weight terms rather than compounding them — harmless here, but it is the same shape
%%     as the mistake this file's ledger entries then had to correct:
%%   ⚠️⚠️ ⑸ LILY#'s ADVANCE IS NOT LINEAR IN THE EM, and a first draft of the four entries
%%     decomposed their residuals ON THE ASSUMPTION THAT IT IS. Scaling the 2.8 reading of
%%     "Fine" down by 2.2/2.8 gives 5.070423959; POISONING PlainTextFontSize to 2.2 and
%%     measuring gives 5.019066141, which is LilyPond's TBF to the ninth digit. The
%%     difference invented a 'face/rounding' term of +0.065 that does not exist — Skia
%%     rounds each glyph at its own ppem exactly as Pango does at LilyPond's, so the two
%%     roundings do not commute with a scale factor. ⇒ THE ENTRIES ARE DECOMPOSED BY POISON
%%     (HANDOFF §5.3), not by arithmetic on this table, and the poison also measured the END
%%     STATE: with em 2.2 AND font-shape italic, mark.jump.width.fine and
%%     mark.jump.width.ds-al-coda read 0.000000000, mark.pedal.width.sostenuto reads one
%%     device pixel, and mark.pedal.width.sustain does not move at all.
%%
%% NOTE: inside #(...) the comment character is `;`, not `%%`.

#(define (probe-grob-row n i g)
   (let* ((nm (assq-ref (ly:grob-property g 'meta) 'name))
          (sys (ly:grob-system g))
          (l (+ (ly:grob-relative-coordinate g sys X)
                (car (ly:grob-extent g g X))))
          (r (+ (ly:grob-relative-coordinate g sys X)
                (cdr (ly:grob-extent g g X)))))
     (format #t "PROBEV GROB ~a ~a name=~a text=~s size=~s shape=~s series=~s x=(~a . ~a) w=~a\n"
             n i nm
             (ly:grob-property g 'text)
             (ly:grob-property g 'font-size)
             (ly:grob-property g 'font-shape)
             (ly:grob-property g 'font-series)
             l r (- r l))
     ;; ASK THE STENCIL WHAT IT DREW. For a text grob the expression carries the resolved
     ;; face name and the em IN MILLIMETRES, which is the size reading this probe exists
     ;; for; for the sustain pedal it carries named music glyphs instead, which is the
     ;; mechanism reading. Either way it is the drawing's own account of itself.
     (let ((st (ly:grob-property g 'stencil)))
       (if (ly:stencil? st)
           (format #t "PROBEV STENCIL ~a ~a name=~a ~s\n"
                   n i nm (ly:stencil-expr st))))))

#(define (probe-dump-pages layout pages)
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let ((sg (ly:prob-property (car ls) 'system-grob)))
                   (if (ly:grob? sg)
                       (let ((all (ly:grob-object sg 'all-elements)))
                         (if (ly:grob-array? all)
                             (for-each
                              (lambda (g)
                                (if (memq (assq-ref (ly:grob-property g 'meta) 'name)
                                          '(JumpScript TextScript SostenutoPedal
                                            UnaCordaPedal SustainPedal))
                                    (probe-grob-row n i g)))
                              (ly:grob-array->list all)))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

%% The serif font is pinned exactly as text-advance.ly and textscript-ink.ly pin it: on the
%% svg backend fonts.serif falls back to whatever fontconfig resolves on this machine
%% (ly/paper-defaults-init.ly:174-177), and a probe about an EM would then be measuring that
%% machine's font. "LilyPond Serif" resolves to C059, which is the face text-advance.ly's
%% nine-digit readings are in — the controls here are only controls if they are the same
%% face those readings pinned.
probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEV BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

%% The music is spelled out per book rather than built by a function, for text-advance.ly's
%% reason: a probe is read to check that the two sides engrave the same thing, and a \score
%% written out is what a reader can compare with LpGeometryProbes without running anything.
%% c''4 takes a DOWN stem, so nothing but the staff's own line stands under an above-staff
%% script and the X reading is never a stem's.

%% JMF — THE MARK ITSELF. \fine puts a JumpScript carrying the fineText default "Fine"
%%     (ly/engraver-init.ly:924), and finalFineTextVisibility is what lets it survive.
%%     ⚠️ MEASURED THE HARD WAY, 2026-08-18: the first run of this file had no such
%%     override and the book printed NO GROB ROW AT ALL. That is not a probe that failed
%%     to compile — LILYPOND-REF: lily/jump-engraver.cc:228-237 finalize, `if (fine_text_
%%     && !final_fine_text_visibility_) fine_text_->suicide ()`, with the property
%%     defaulting to #f (:41 final_fine_text_visibility_ = false; scm/define-context-
%%     properties.scm:397-398). LilyPond DELIBERATELY DOES NOT PRINT "Fine" at the written
%%     end of the music: the final bar line already says it. The empty book was the
%%     measurement (HANDOFF §5.3: a zero arrives wearing the face of "not measured"), and
%%     it is kept as JMN below rather than deleted, because Lily# has no such rule.
\book {
  \probeTag "JMF"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4 c'' c'' c'' | c''1 \fine }
           \layout { \context { \Score finalFineTextVisibility = ##t } } }
}

%% JMN — THE SAME BOOK AT THE DEFAULT. Identical in every character but the override, so
%%     the difference between the two dumps is the suicide and nothing else. It prints no
%%     JumpScript row, and that absence is this file's only reading that is an absence:
%%     stated here so the next reader does not take it for a broken probe.
\book {
  \probeTag "JMN"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4 c'' c'' c'' | c''1 \fine } }
}

%% TIF — ITS CONTROL. The same string as a TextScript at the paper's text-font-size in
%%     italic: if the JumpScript declares no size and no series, these two are one number.
\book {
  \probeTag "TIF"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4^\markup \italic "Fine" c'' c'' c'' |
                        c''1 \bar "|." } }
}

%% TBF — THE WEIGHT, priced alone. Same em, same slant, bold: the term Lily# adds on top of
%%     the size term, which a single reading of the mark could not separate from it.
\book {
  \probeTag "TBF"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' |
                        c''4^\markup \bold \italic "Fine" c'' c'' c'' | c''1 \bar "|." } }
}

%% JMJ / TIJ / TBJ — THE LONG STRING, the same three readings. "D.S. al Coda" is the string
%%     HANDOFF §1 measured the largest reservation error on (4.233770079 ss), it carries
%%     spaces and periods rather than only letters, and its length multiplies any per-glyph
%%     term — so if the mark's width is a SIZE the two strings' ratios agree, and if it is a
%%     per-glyph accumulation they do not.
\book {
  \probeTag "JMJ"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4 c'' c'' c'' |
                        c''1 \jump "D.S. al Coda" \bar "|." } }
}

\book {
  \probeTag "TIJ"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' |
                        c''4^\markup \italic "D.S. al Coda" c'' c'' c'' | c''1 \bar "|." } }
}

\book {
  \probeTag "TBJ"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' |
                        c''4^\markup \bold \italic "D.S. al Coda" c'' c'' c'' |
                        c''1 \bar "|." } }
}

%% PSO / TIS — THE SOSTENUTO PEDAL, which IS text (ly:text-interface::print, font-shape
%%     italic, no series, no size) and so must read exactly like the JumpScript pair.
\book {
  \probeTag "PSO"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4\sostenutoOn c'' c'' c'' |
                        c''1\sostenutoOff \bar "|." } }
}

\book {
  \probeTag "TIS"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' |
                        c''4^\markup \italic "Sost. Ped." c'' c'' c'' | c''1 \bar "|." } }
}

%% TBS — THE SOSTENUTO STRING'S WEIGHT, added on the second run (2026-08-18) because the
%%     ledger entry was about to carry an ESTIMATE. Without it the sostenuto residual could
%%     only be split by borrowing the bold/italic ratio the two navigation strings measured
%%     (1.082 and 1.114) — and those two disagree, which is exactly what says the term is a
%%     face table and not a scalar, i.e. that borrowing it is not allowed. One more book is
%%     cheaper than a range in a `why` (HANDOFF §5.0: 総計の引き算で名前を付けてはいけない).
\book {
  \probeTag "TBS"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' |
                        c''4^\markup \bold \italic "Sost. Ped." c'' c'' c'' |
                        c''1 \bar "|." } }
}

%% PSU / TBP — THE SUSTAIN PEDAL, which is NOT text: Sustain_pedal::print pastes the music
%%     font's pedal.Ped and pedal.. glyphs edge to edge. TBP beside it is what Lily# draws
%%     in its place — an upright bold serif string — so the pair states the mechanism gap in
%%     one subtraction instead of leaving it to be discovered as a size error later.
\book {
  \probeTag "PSU"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4\sustainOn c'' c'' c'' |
                        c''1\sustainOff \bar "|." } }
}

\book {
  \probeTag "TBP"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' |
                        c''4^\markup \bold "Ped." c'' c'' c'' | c''1 \bar "|." } }
}
