# Lily# Sound Font (MuseScore General)

The sound [Lily#](https://marketplace.visualstudio.com/items?itemName=yotsuda.lilysharp) plays
your scores with. It is one file and nothing else: no commands, no settings, no code.

**What is in it.** `MuseScore_General.sf3`, version 0.2 — the General MIDI sound font that
MuseScore 2.2 and 3 shipped as their default — **exactly as its authors published it**
(SHA-256 `5b85b6c2c61d10b2b91cddd41efcce7b25cd31c8271d511c73afafbef20b6fa3`, the file at
<https://ftp.osuosl.org/pub/musescore/soundfont/MuseScore_General/>).

**Whose work it is.** Not ours. MuseScore_General was made by S. Christian Collins from
FluidR3Mono (Michael Cowgill), itself from Fluid R3 (Frank Wen), with instruments from Ethan Winer
and Michael Schorsch, and is shared under the MIT license. Their notices, their README and their
changelog are in this package as they wrote them: [the license](MuseScore_General_License.md),
[the README](MuseScore_General_Readme.md), [the changelog](MuseScore_General_Changelog.md).

**This is not an official MuseScore distribution.** MuseScore is a trademark of its owner; this
package is published by the Lily# project, which is not affiliated with it. The name stands here
to say what the file is.

## Why it is a package of its own

A sound font is large (38 MB) and does not change. In a package of its own it is downloaded once,
and Lily#'s own updates stay small.

## Using it

Install it beside Lily#. Lily# versions after 0.16.0 look for it and play through it by default;
earlier ones play as before. It gives way to a font you choose yourself
(`lilysharp.playback.soundFont`, **Lily#: Choose Sound Font…**) and to a MIDI port
(`lilysharp.playback.midiOutput`, **Lily#: Choose MIDI Output…**). Without it, Lily# plays the
General MIDI bank your computer has (Windows, macOS) or plain tones.

## Recordings you make with it

A sound you export through this font is made from its samples. The MIT license asks that its
copyright notice go with "copies or substantial portions" of the font; whether a recording of
your own music is such a portion is not for this page to settle. MuseScore's users have published
recordings made with this font for years. If it matters to you, credit the font — "MuseScore_General
sound font, S. Christian Collins et al., MIT license" — or ask someone who can advise you.