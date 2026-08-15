\version "2.16.0"

% Dump the realized pitches of every chordmode entry in chord-name-entry.ly.
% Harness-side only (never a corpus twin): \displayLilyMusic prints the
% note-chord realization to stdout.

\displayLilyMusic \chordmode {
    c1
    c:7
    c:m
    c:m7
    c:aug
    c:maj7
    c:dim
    c:dim7
    c:sus4
    c:sus2
    c:6
    c:m6
    c:7sus4
    c:3-
    c:3+
    c:5+.3-
    c:7
    c:9
    c:11
    c:13
    c:m13
    c:7^5
    c^3
    c/g
    c/gis
    c/a
    c/+f
    c/+g
}
