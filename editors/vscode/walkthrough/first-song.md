# A first song

Four statements, one job each:

```lily#
part melody { instrument alto  lyrics words }   // the part names its words
lyrics words { う さ ぎ | お い し | }            // the words, a syllable a note
score { staff melody  lyrics words }             // the row prints them
singer female on melody                          // this line sings them
```

**Open a First Song** opens such a song, in a language your voices sing: ふるさと in
Japanese, *Twinkle, Twinkle, Little Star* in English, 送别 in Chinese or 아리랑 in Korean,
each with its title. All four are in the public domain. On a computer with an NVIDIA GPU it
is the whole song, half a minute of it; without one it is the song's opening, because a voice
on a processor takes several times as long to sing as the song lasts. The step's two links
open either. Words that are not Japanese say their language:
`lyrics words language english { Twin- kle, twin- kle, | lit- tle star, | }`.

Open its preview (the icon at the editor's top
right, or **Lily#: Open Preview to the Side**) and press **Play**: the first time, the
engine loads the voice, which can take up to a minute; after that a change is heard at once.

After `singer "` the editor lists every voice installed. Taking one from the list says a
short phrase in that voice.

- `singer female on melody` — the default voice of that gender
- `singer "voicevox/ずんだもん-ノーマル" on melody` — that voice
- `singer female on melody sings "ラ"` — every note on ラ, with no words written

A line no installed voice can sing is played on the part's instrument, and Lily# says why.
