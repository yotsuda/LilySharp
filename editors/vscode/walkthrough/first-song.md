# A first song

Four statements, one job each:

```lily#
part melody { instrument alto  lyrics words }   // the part names its words
lyrics words { あ か る い | そ ら | }            // the words, a syllable a note
score { staff melody  lyrics words }             // the row prints them
singer female on melody                          // this line sings them
```

**Open a First Song** opens such a song. Open its preview (the icon at the editor's top
right, or **Lily#: Open Preview to the Side**) and press **Play**: the first time, the
engine loads the voice, which can take up to a minute; after that a change is heard at once.

After `singer "` the editor lists every voice installed. Taking one from the list says a
short phrase in that voice.

- `singer female on melody` — the default voice of that gender
- `singer "voicevox/ずんだもん-ノーマル" on melody` — that voice
- `singer female on melody sings "ラ"` — every note on ラ, with no words written

A line no installed voice can sing is played on the part's instrument, and Lily# says why.
