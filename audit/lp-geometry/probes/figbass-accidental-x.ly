\version "2.26.0"
%% SCRATCH — 第2便の remark が言い過ぎていないかの検算。
%%
%% remark は「図形は列の LEFT EDGE に左揃え」と書いた。だが測ったのは FBA で、その本では
%% NoteHead・Stem・NoteColumn・BassFigure の左端が全部一致していた——4 つが同じ点なのだから、
%% そのどれに揃っているのかは区別できていない。
%%
%% 臨時記号は NoteColumn の左端を符頭より左へ伸ばす。だから:
%%   図形が符頭に揃う  -> 図形の x は NoteHead の x と一致する
%%   図形が列に揃う    -> 図形の x は Accidental の x と一致する（符頭より左）
%% 一発で割れる。

#(define (probe-dump-pages layout pages)
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let* ((sys (car ls)))
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (memq nm '(BassFigure NoteHead NoteColumn Accidental
                                                   AccidentalPlacement Stem))
                                        (format #t "PROBEX ~a name=~a boxleft=~a\n"
                                                n nm
                                                (+ (ly:grob-relative-coordinate g sg X)
                                                   (car (ly:grob-extent g g X)))))))
                                (ly:grob-array->list all))))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

\paper {
  ragged-bottom = ##t indent = 0
  page-post-process = #(lambda (layout pages)
                         (format #t "\nPROBEX BOOK FBX\n")
                         (probe-dump-pages layout pages))
}
\header { tagline = ##f }

\score {
  \new Staff \with { \consists "Figured_bass_engraver" } {
    \clef bass
    \fixed c' {
      \time 4/4
      <<
        { \stemDown cis,,2 cis,,2 | }
        \figuremode { <5 3>2 <6>2 | }
      >>
    }
  }
  \layout { indent = 0\mm }
}
