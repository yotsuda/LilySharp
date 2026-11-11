# Releasing the Lily# Sound Font extension

For whoever publishes it (the owner). Nothing here is in the package (`.vscodeignore`).

1. `./Get-SoundFont.ps1` — brings `soundfont/MuseScore_General.sf3` from the MuseScore project's
   mirror and checks its SHA-256 against the one the README gives. The font is not in git.
2. `npx @vscode/vsce package` in this folder — a VSIX of 34 MB (built and installed on the owner's machine 2026-11-11; Lily#'s own is 33). If the Marketplace turns it
   down for its size, that is the limit this package was not tried against (Lily#'s own VSIX is
   33 MB): say so in docs/HANDOFF.md and the plan is to be thought again.
3. Install the VSIX in a VS Code that has Lily#, open a score and Play: the status of the synth
   in the Lily# Extension output names `MuseScore_General.sf3`, and **Lily#: Choose Sound Font…**
   lists it as "(the Lily# Sound Font extension)".
4. Publish it (`npx @vscode/vsce publish`).
5. ONLY THEN release the Lily# that names it. Lily#'s package.json carries `"extensionPack":
   ["yotsuda.lilysharp-soundfont"]` (owner's decision 2026-11-11: a pack, not a dependency — Lily#
   plays without the font, so the font's package must never be what keeps Lily# from being
   installed). What a pack does was tried on the owner's machine with dummy parents, from VSIX files:
   the parent installs whether or not the member can be had; the member can be removed alone and the
   parent stays; removing the parent removes the member too. A dependency, tried beside it, refuses
   the member's removal ("… depends on this"). NOT tried, because it needs the Marketplace: that
   installing or updating Lily# there brings the member — look at that on the first release.

A new version of the font: read its changelog upstream, change the hash in `Get-SoundFont.ps1` and
in the README together with the version here and in CHANGELOG.md, and copy the upstream license,
README and changelog files again.

Courtesy, not a condition of the license: a note to the font's author (S. Christian Collins) that
it is carried here, with the link.