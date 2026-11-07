// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// The MP4 export's ffmpeg call (owner's request 2026-11-07: a video of the score, the notes lit
// as they sound, with the preview's sound). The server draws the pictures and lists them for
// ffmpeg's concat demuxer (LilySharp.Core.Export.ScoreVideo); the editor renders the sound as a
// WAV (audioRender.ts) and has ffmpeg join the two. Lily# bundles no ffmpeg: it is the user's,
// on the PATH or named in lilysharp.export.ffmpegPath.

/** The video's frame rate: the pictures' (ScoreVideoOptions.Fps). */
export const VIDEO_FPS = 30;

/** The program to run: the setting when it names one, else `ffmpeg` from the PATH. */
export function ffmpegProgram(setting: string | undefined): string {
    const named = (setting ?? '').trim();
    return named.length > 0 ? named : 'ffmpeg';
}

/**
 * The ffmpeg arguments that write `output`: the pictures `frames` lists, and the sound `wav`
 * started `lead` seconds in, as the pictures hold the first page still that long — `seconds` in
 * all. H.264 and AAC in an MP4, the index at the front so a page can play it as it loads.
 */
export function videoArgs(frames: string, wav: string, output: string, lead: number, seconds: number,
    fps = VIDEO_FPS): string[] {
    const delayMs = Math.round(lead * 1000);
    return [
        '-hide_banner', '-loglevel', 'error', '-y',
        '-f', 'concat', '-safe', '0', '-i', frames,
        '-i', wav,
        '-filter_complex', `[1:a]adelay=${delayMs}:all=1[a]`,
        '-map', '0:v', '-map', '[a]',
        '-r', String(fps), '-t', seconds.toFixed(3),
        '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '20', '-tune', 'stillimage',
        '-c:a', 'aac', '-b:a', '192k',
        '-movflags', '+faststart',
        output,
    ];
}

/** What to tell the writer when ffmpeg cannot be run: where to get it and how to name it. */
export function ffmpegMissing(program: string): string {
    return `Lily#: Export as MP4 needs ffmpeg, and "${program}" could not be run. `
        + 'Install it (Windows: winget install Gyan.FFmpeg; Mac: brew install ffmpeg; Linux: your package manager) '
        + 'or set lilysharp.export.ffmpegPath to the program.';
}
