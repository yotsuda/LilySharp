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

// Writes the MP4s an export names (lilysharp/export, mp4, playbackOnly): for each, the server
// draws the pictures of the notes it will sound (lilysharp/videoFrames — the practice menu
// already applied to them), the sound is rendered as the preview plays it, and ffmpeg joins the
// two. The arguments are videoExportCore.ts.

import * as cp from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import { AudioJob, renderAudioFiles } from './audioRender';
import { WAV_SAMPLE_RATE, frameCount } from './audioRenderCore';
import { videoArgs } from './videoExportCore';

/** The server's answer to lilysharp/videoFrames (VideoFramesResponse). */
export interface FramesAnswer {
    Frames?: string | null;
    WorkDirectory?: string | null;
    Lead?: number;
    Warnings?: string[] | null;
    Error?: string | null;
}

/** Asks the server for one job's pictures: its notes and how long its sound runs. */
export type DrawFrames = (job: AudioJob, seconds: number) => Promise<FramesAnswer>;

/** Runs a program to its end: its error output when it fails, null when it succeeds. */
function run(program: string, args: string[]): Promise<string | null> {
    return new Promise(resolve => {
        const child = cp.spawn(program, args, { windowsHide: true });
        let stderr = '';
        child.stderr.on('data', (d: Buffer) => { stderr += d.toString(); });
        child.on('error', e => resolve(String(e)));
        child.on('close', code => resolve(code === 0 ? null : (stderr.trim() || `exit code ${code}`)));
    });
}

/** True when `program` runs (`-version`): asked before anything is rendered. */
export async function ffmpegRuns(program: string): Promise<boolean> {
    return (await run(program, ['-version'])) === null;
}

/**
 * Writes every job: its pictures drawn by the server for its notes, its sound through the sound
 * font at `bankPath` into the pictures' folder, then ffmpeg joins them into its MP4. Every
 * folder the server drew into is deleted afterwards, written or not — a failure part-way leaves
 * none behind. Throws with the server's or ffmpeg's own words when either fails.
 */
export async function writeVideos(jobs: readonly AudioJob[], bankPath: string, ffmpeg: string,
    drawFrames: DrawFrames): Promise<void> {
    const folders: string[] = [];
    try {
        for (const job of jobs) {
            const seconds = frameCount(job.Notes) / WAV_SAMPLE_RATE;
            const frames = await drawFrames(job, seconds);
            if (frames.WorkDirectory) { folders.push(frames.WorkDirectory); }
            if (frames.Error || !frames.Frames || !frames.WorkDirectory) {
                throw new Error(`the pictures of ${path.basename(job.OutputPath)} were not drawn: ${frames.Error ?? 'no list came back'}`);
            }
            const wav = path.join(frames.WorkDirectory, 'sound.wav');
            await renderAudioFiles([{ OutputPath: wav, Notes: job.Notes }], bankPath, 'wav');
            const lead = frames.Lead ?? 0;
            fs.mkdirSync(path.dirname(job.OutputPath), { recursive: true });
            const failure = await run(ffmpeg, videoArgs(frames.Frames, wav, job.OutputPath, lead, lead + seconds));
            if (failure !== null) {
                throw new Error(`ffmpeg could not write ${path.basename(job.OutputPath)}: ${failure.split(/\r?\n/).slice(-3).join(' ')}`);
            }
        }
    } finally {
        for (const folder of folders) { fs.rmSync(folder, { recursive: true, force: true }); }
    }
}
