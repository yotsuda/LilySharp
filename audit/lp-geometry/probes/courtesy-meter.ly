\version "2.26.0"
%
% WHAT STANDS AT THE END OF A LINE WHEN THE NEXT ONE CHANGES METER?
%
% THE DEFECT THIS WAS OPENED FOR (2026-08-02, session 75). Lily# printed the courtesy KEY at
% a line end but never the courtesy METER, so a book that goes 2/4 -> 4/4 across a break lost
% the C at the end of the previous line. LilyPond prints it, and the rule is not "key changes
% are special":
%   scm/define-grobs.scm:3922-3953 — TimeSignature's own break-visibility is `all-visible`,
%     so a signature prints at the END of a line as well as at the start of the next.
%   lily/time-signature-engraver.cc:114-118 Time_signature_engraver::process_music — the
%     grob default is overridden with `initialTimeSignatureVisibility` for the FIRST
%     signature only (guarded by `scm_is_null (last_spec_)`).
%   ly/engraver-init.ly:867 — that property is `end-of-line-invisible`.
% ⇒ the INITIAL meter never shows at a line end; every CHANGED one does. Measured three ways:
%   both change → cancellation, new key, meter;  meter only → meter alone;  neither → bare.
%
% ⚠️ THE BARE CASE IS PART OF THE READING AND HAS NO POINT HERE, because there is nothing to
% measure when nothing is drawn. It is held as an assertion instead (Lily# tests) — a corpus
% that only measures what IS drawn cannot see a courtesy that should NOT have been.
%
% Output, one line per grob:
%   PROBECM <name> <grob> x=<x in the system> ext=<X-extent> breakdir=<-1 end | 1 begin>
\paper { indent = 0 ragged-right = ##f line-width = 60 }

#(define (dump name what)
   (lambda (g)
     (format #t "\nPROBECM ~a ~a x=~a ext=~a breakdir=~a\n" name what
             (ly:grob-relative-coordinate g (ly:grob-system g) X)
             (ly:grob-property g 'X-extent)
             (ly:item-break-dir g))))

sweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with {
        \override TimeSignature.after-line-breaking = #(dump name "TIME")
        \override KeySignature.after-line-breaking = #(dump name "KEY")
        \override KeyCancellation.after-line-breaking = #(dump name "KEYCANCEL")
        \override BarLine.after-line-breaking = #(dump name "BAR")
      } { $music } #})

% A. THE METER ALONE. Only the meter changes at the break, so the line end carries the C and
%    nothing else — the gap is bar-line ink right edge → the meter, with no key in between.
\score { \sweep "CMT" { \clef bass \key ees \major \time 2/4 r2 | \break \time 4/4 d2 e | }
         \layout {} }

% B. BOTH CHANGE, which is the shape a real book has (scratch/ベースタブLy/repro.lys, section D
%    ending 2/4 and section B3 opening 4/4 in a new key). The line end carries cancellation,
%    new key, THEN the meter, and the first of those is what this book measures off the bar.
\score { \sweep "CMK" { \clef bass \key ees \major \time 2/4 r2 | \break \key a \major \time 4/4 d2 e | }
         \layout {} }

% ---------------------------------------------------------------------------------------
% WHAT THIS FILE FOUND (2026-08-02, session 75)
%
%   CMT  BAR 31.003307 ext (0 . 0.19) → ink right 31.193307
%        TIME 31.943307                                        gap 0.750000
%   CMK  BAR 23.353507 ext (0 . 0.19) → ink right 23.543507
%        KEYCANCEL 24.543507                                   gap 1.000000
%        KEY 27.493307 (cancellation ink ends 26.993307)       gap 0.500000
%        TIME 31.943307 (key ink ends 30.793307)               gap 1.150000
%
% ⇒ THE TWO GAPS OFF THE BAR LINE ARE NOT THE SAME NUMBER (0.75 against 1.00). LilyPond has no
%   single "space after the bar line": each break-aligned grob brings its own space-alist
%   entry. Lily# spelled one constant (0.8) for both, which is why CMK opens non-zero.
% ⇒ 1.150000 is ALSO what the line-START prefix puts between key and meter (7.603400 →
%   8.753400 on the same book), which is what says it is break-align spacing and not
%   something the courtesy invented.
