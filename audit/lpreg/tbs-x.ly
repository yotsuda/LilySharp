\version "2.26.0"
probeBr = \override TupletBracket.after-line-breaking =
  #(lambda (g)
     (let* ((sys (ly:grob-system g))
            (cols (ly:grob-object g 'note-columns)))
       (format #t "PROBEX BRACKET pos=~a Xpos=~a\n"
               (ly:grob-property g 'positions)
               (ly:grob-property g 'X-positions))
       (if (ly:grob-array? cols)
           (for-each
            (lambda (col)
              (let ((stem (ly:grob-object col 'stem)))
                (format #t "PROBEX COL relx=~a colext=(~a . ~a) stemext=(~a . ~a) yext=(~a . ~a)\n"
                        (ly:grob-relative-coordinate col sys X)
                        (car (ly:grob-extent col sys X))
                        (cdr (ly:grob-extent col sys X))
                        (car (ly:grob-extent stem sys X))
                        (cdr (ly:grob-extent stem sys X))
                        (car (ly:grob-extent col sys Y))
                        (cdr (ly:grob-extent col sys Y)))))
            (ly:grob-array->list cols)))))

\book {
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { \probeBr c''2 \tuplet 3/2 { g'4 e' c' } \bar "|." } }
}
\book {
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { \probeBr c''2 \tuplet 3/2 { c'4 e' g' } \bar "|." } }
}
