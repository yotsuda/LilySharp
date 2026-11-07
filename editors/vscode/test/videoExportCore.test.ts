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

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import { VIDEO_FPS, ffmpegMissing, ffmpegProgram, videoArgs } from '../src/videoExportCore';

// The MP4 export (owner's request 2026-11-07): the server's pictures and the preview's sound,
// joined by the user's ffmpeg.
describe('the MP4 export\'s ffmpeg', () => {
    it('is the setting when it names one, else ffmpeg from the PATH', () => {
        assert.equal(ffmpegProgram(''), 'ffmpeg');
        assert.equal(ffmpegProgram('   '), 'ffmpeg');
        assert.equal(ffmpegProgram(undefined), 'ffmpeg');
        assert.equal(ffmpegProgram(' C:\\tools\\ffmpeg.exe '), 'C:\\tools\\ffmpeg.exe');
    });

    it('joins the pictures and the sound, the sound started after the still lead', () => {
        const args = videoArgs('/w/frames.txt', '/w/sound.wav', '/out/song.mp4', 1.5, 103.25);
        const after = (flag: string) => args[args.indexOf(flag) + 1];
        assert.deepEqual(args.slice(args.indexOf('-f'), args.indexOf('-f') + 6), ['-f', 'concat', '-safe', '0', '-i', '/w/frames.txt']);
        assert.ok(args.includes('/w/sound.wav'));
        assert.equal(after('-filter_complex'), '[1:a]adelay=1500:all=1[a]');
        assert.equal(after('-t'), '103.250');
        assert.equal(after('-r'), String(VIDEO_FPS));
        assert.equal(after('-c:v'), 'libx264');
        assert.equal(after('-pix_fmt'), 'yuv420p', 'a picture every player can show');
        assert.equal(after('-c:a'), 'aac');
        assert.equal(args[args.length - 1], '/out/song.mp4');
        assert.ok(args.includes('-y'), 'the save dialog already asked before overwriting');
    });

    it('says where to get ffmpeg and how to name it when it does not run', () => {
        const message = ffmpegMissing('ffmpeg');
        assert.match(message, /winget install Gyan\.FFmpeg/);
        assert.match(message, /lilysharp\.export\.ffmpegPath/);
    });
});
