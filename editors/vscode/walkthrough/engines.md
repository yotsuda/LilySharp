# A singing engine

One engine is enough. Each is its makers' work, downloaded from them and used under their
terms: nothing here comes with Lily# or Cantoria.

**A GPU makes the difference.** NEUTRINO sings on an NVIDIA GPU, and a DiffSinger voice on any
DirectX 12 GPU on Windows: there 43 seconds of song are sung in about 7. With no such GPU a voice
sings on the processor, and NEUTRINO then takes several times as long as the song itself: on a
laptop with Intel graphics it sang a 36-second part in about 2¼ minutes with the package that has
the tuning tool, and 4 with the plain one. What was sung is kept, so after the first time only a
phrase you change is sung again. On such a PC, have the editor sing only when you press ▶: in
the settings, search for `singerPrepare` and choose **onPlay**.

| Engine | What it is | Sings |
|---|---|---|
| **[DiffSinger](https://github.com/openvpi/DiffSinger)** | Voices published one by one by their authors, as folders | Japanese, and English, Chinese or Korean where the voice has a dictionary for them |
| **[NEUTRINO](https://studio-neutrino.com/)** | One download with a voice in it; more voices from its site. Windows only | Japanese |
| **[VOICEVOX](https://voicevox.hiroshiba.jp/)** | One download, many voices. The quickest start | Japanese |

## DiffSinger

1. Make a folder for voices, `C:\voices` for one.
2. Download a voice in the OpenUtau format from its author, and read the author's terms.
   Unpack it into that folder, so that the voice is a folder of its own holding a
   `dsconfig.yaml`: `C:\voices\TIGER\dsconfig.yaml`. The `Singers` folder of
   [OpenUtau](https://github.com/stakira/OpenUtau), if you have one, is such a folder already.
3. Choose `C:\voices` with **Choose DiffSinger Voices Folder**. (OpenUtau's own `Singers`
   folder is found by **Find Engines on This Computer**.)
4. If the voice has no `dsvocoder` folder of its own, it needs a vocoder, the part that
   turns what the voice computes into sound. Download the `.oudep` file its readme names
   (the usual one is `nsf_hifigan`, among the DiffSinger community's
   [vocoders](https://github.com/openvpi/vocoders)), rename it to `.zip`, unpack it into a folder, and run **Cantoria: Choose
   Vocoder Folder… (DiffSinger)** from the command palette. Lily# tells you when a voice
   you chose needs one.

## NEUTRINO

1. Download NEUTRINO (Tau, v3.2 or later) from [studio-neutrino.com](https://studio-neutrino.com/)
   and unpack it: the
   Windows package **with the tuning tool** (調声支援ツール同梱版). That package keeps the
   voice loaded, so a phrase you change is sung again in well under a second; the plain
   package sings too, but loads the voice for every phrase.
2. Choose the folder that holds `bin`, `model` and `settings` with **Choose NEUTRINO
   Folder** (in the package with the tuning tool, the inner folder `NEUTRINO 調声支援ツール`).
3. To add a voice, download it from the same site (歌声ライブラリ, its **v3 Tau** version),
   and put its folder, the one that holds `info.toml`, under NEUTRINO's `model` folder.

## VOICEVOX

1. Download VOICEVOX from [voicevox.hiroshiba.jp](https://voicevox.hiroshiba.jp/) and install it,
   or unpack its zip. Each
   character has terms of its own, and a published work credits `VOICEVOX:name`.
2. Press **Find Engines on This Computer**: an installed VOICEVOX is found where its installer
   left it. If you unpacked the zip somewhere of your own, choose the folder that holds
   `vv-engine` with **Choose VOICEVOX Folder**. Cantoria then starts the engine when one of its
   voices is needed and stops it when VS Code closes. With VOICEVOX itself open, its voices are
   found with no folder chosen.
