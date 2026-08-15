\layout {
  \context {
    \Score
    \override NoteColumn.after-line-breaking =
      #(lambda (g)
        (let ((sys (ly:grob-system g))
              (col (ly:item-get-column g)))
          (format #t "NC sysx=~a colx=~a heads=~a\n"
                  (ly:grob-relative-coordinate g sys X)
                  (ly:grob-relative-coordinate col sys X)
                  (map (lambda (h)
                         (cons (ly:grob-property h 'staff-position)
                               (ly:grob-extent h h X)))
                       (ly:grob-array->list (ly:grob-object g 'note-heads))))))
  }
}
