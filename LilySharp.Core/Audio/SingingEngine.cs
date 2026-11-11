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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using LilySharp.Core.Midi;

namespace LilySharp.Core.Audio;

/// <summary>
/// What sings a part's words for <c>lysc wav --singer</c>: a program given a sung score (a
/// JSON file, docs/singing-design.md §3) that writes a WAV whose first sample is the score's
/// zero. <see cref="CantoriaProgram"/> is the one shipped; a test stands another in. Nothing
/// is linked: the engine is a separate process, as the editor's is a separate extension
/// (singing-design §6). LILYSHARP-OWN.
/// </summary>
public abstract class SingingEngine
{
    /// <summary>What the export reports the voice as ("neutrino/MERROW", or the program's own default).</summary>
    public abstract string Name { get; }

    /// <summary>
    /// Sings the score written at <paramref name="scoreJsonPath"/> into <paramref name="outputWavPath"/>.
    /// Answers null when the WAV is there, else one sentence saying why not — the export then
    /// leaves the instrument in the voice's place and says so in its warnings.
    /// </summary>
    public abstract string? Sing(string scoreJsonPath, string outputWavPath);

    /// <summary>This engine singing in the voice <paramref name="voice"/> (<c>engine/name</c>), for a
    /// part the file names a singer of; an engine with one voice is itself.</summary>
    public virtual SingingEngine WithVoice(string voice) => this;

    /// <summary>
    /// Sings <paramref name="voice"/> and reads the sound back: the score written as Cantoria's
    /// JSON (<see cref="SungScoreJson"/>) under a folder of its own in the temp directory, the
    /// engine run, the WAV decoded (<see cref="WavReader"/>) and the folder removed. An engine
    /// that fails answers its sentence in <c>Error</c> and no sound.
    /// </summary>
    public (MonoAudio? Sound, string? Error) Sing(SungVoice voice, int sampleRate)
    {
        string dir = Path.Combine(Path.GetTempPath(), "lilysharp-sing", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string json = Path.Combine(dir, "score.json");
            string wav = Path.Combine(dir, "voice.wav");
            File.WriteAllText(json, SungScoreJson.Write(voice, sampleRate), new UTF8Encoding(false));
            string? error;
            try
            {
                error = Sing(json, wav);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return (null, e.Message);
            }
            if (error != null)
                return (null, error);
            if (!File.Exists(wav))
                return (null, $"{Name} wrote no WAV");
            try
            {
                return (WavReader.Read(File.ReadAllBytes(wav)), null);
            }
            catch (InvalidDataException e)
            {
                return (null, $"{Name}'s WAV could not be read: {e.Message}");
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

/// <summary>
/// The <c>cantoria</c> command line (<c>C:\MyProj\cantoria</c>; Apache-2.0, the owner's, never
/// linked): <c>cantoria sing score.json -o voice.wav [--voice engine/name]</c>. Where its
/// engines and voices are is its own configuration (its environment, its
/// <c>cantoria.json</c>), as the Cantoria extension's settings are for the preview; the
/// user names the voice here as <c>engine/name</c> (<c>neutrino/MERROW</c>,
/// <c>diffsinger/TIGER</c>) or lets the program choose its default.
/// </summary>
public sealed class CantoriaProgram : SingingEngine
{
    /// <summary>The program to run: a path, or a name found on the PATH.</summary>
    public string Program { get; }

    /// <summary>The voice asked for (<c>engine/name</c>), or null for the program's default.</summary>
    public string? Voice { get; }

    public CantoriaProgram(string program, string? voice = null)
    {
        Program = program;
        Voice = string.IsNullOrWhiteSpace(voice) ? null : voice;
    }

    public override string Name => Voice ?? Path.GetFileNameWithoutExtension(Program);

    public override SingingEngine WithVoice(string voice) => new CantoriaProgram(Program, voice);

    /// <summary>
    /// Why <paramref name="program"/> cannot be run, or null: a path that names no file is
    /// refused here, before any score is rendered; a bare name is left to the PATH.
    /// </summary>
    public static string? Problem(string program)
    {
        if (string.IsNullOrWhiteSpace(program))
            return "name a cantoria program with --singer <program>";
        bool isPath = program.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
            || Path.HasExtension(program) && !program.Equals("cantoria", StringComparison.OrdinalIgnoreCase);
        if (isPath && !File.Exists(program))
            return $"singer not found: {program}";
        return null;
    }

    public override string? Sing(string scoreJsonPath, string outputWavPath)
    {
        var start = new ProcessStartInfo(Program)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,       // the engine reads no console; closed at once
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("sing");
        start.ArgumentList.Add(scoreJsonPath);
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add(outputWavPath);
        if (Voice != null)
        {
            start.ArgumentList.Add("--voice");
            start.ArgumentList.Add(Voice);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"could not start {Program}");
        process.StandardInput.Close();
        // Both pipes drained while it runs, so a chatty engine cannot fill one and stall.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string err = stderr.GetAwaiter().GetResult();
        _ = stdout.GetAwaiter().GetResult();
        if (process.ExitCode == 0)
            return null;
        // Its last line is the sentence ("cantoria: …"); a silent failure gets the exit code.
        string last = "";
        foreach (var line in err.Split('\n'))
            if (line.Trim().Length > 0) last = line.Trim();
        return last.Length > 0 ? last : $"{Path.GetFileName(Program)} exited with code {process.ExitCode}";
    }
}

/// <summary>
/// A <see cref="SungVoice"/> as the JSON Cantoria reads (its README; docs/singing-design.md §3):
/// seconds, MIDI pitch, the syllable as pronounced; a melisma note carries no lyric; a breath
/// where the voice put one. The same shape the editor sends (<c>singerCore.cantoriaScore</c>),
/// at the speed written.
/// </summary>
public static class SungScoreJson
{
    /// <summary>Seconds the score is moved later before it goes to the engine, so its WAV begins this
    /// long before the score's 0: the room for the first syllable's consonant, which a singer places
    /// before the beat — rendered from the score's 0 it was cut away (owner's report 2026-11-08:
    /// NEUTRINO's さ, its s from −0.103 s to +0.011 s, sounded as た). The editor's
    /// <c>VOICE_LEAD_S</c> is the same number (docs/singing-design.md §3).</summary>
    public const double Lead = 0.3;

    private static readonly JsonWriterOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // kana as kana, not \uXXXX
        Indented = false,
    };

    public static string Write(SungVoice voice, int sampleRate)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, Options))
        {
            w.WriteStartObject();
            w.WriteNumber("sampleRate", sampleRate);
            w.WriteString("language", voice.Language);
            // How the voice sings, as its singer line's `with …` writes it: the engine's own names.
            if (voice.Sing is { Count: > 0 } sing)
            {
                w.WriteStartObject("sing");
                foreach (var (name, value) in sing.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (value.Number is { } number)
                        w.WriteNumber(name, number);
                    else
                        w.WriteString(name, value.Word);
                }
                w.WriteEndObject();
            }
            w.WriteStartArray("notes");
            foreach (var n in voice.Notes)
            {
                // Rounded at the note's ENDS, not its length: a start and a duration rounded apart
                // can put the next note's start a millionth before this one's end (16ths at 132 are
                // repeating decimals), and the engine refuses a score whose notes overlap (owner's
                // report 2026-11-08, 雪やこんこ: "note 23 at 6.591 s starts before note 22 ends").
                double start = Math.Round(n.Start + Lead, 6), end = Math.Round(n.Start + n.Duration + Lead, 6);
                w.WriteStartObject();
                w.WriteNumber("start", start);
                w.WriteNumber("duration", Math.Round(end - start, 6));
                w.WriteNumber("pitch", n.Pitch);
                if (n.Melisma)
                    w.WriteBoolean("melisma", true);
                else if (n.Lyric != null)
                    w.WriteString("lyric", n.Lyric);
                if (n.Breath)
                    w.WriteBoolean("breath", true);
                if (n.Level is { } level)
                    w.WriteNumber("level", level);
                if (n.LevelEnd is { } levelEnd)
                    w.WriteNumber("levelEnd", levelEnd);
                if (n.Inhale)
                    w.WriteBoolean("inhale", true);
                if (n.Hyphen)
                    w.WriteBoolean("hyphen", true);
                // A written vibrato (a trial: a DiffSinger voice sings it; AnnotationValues.Vibrato).
                if (n.Vibrato is { } vibrato)
                    w.WriteNumber("vibrato", vibrato);
                if (n.VibratoRate is { } vibratoRate)
                    w.WriteNumber("vibratoRate", vibratoRate);
                // What an `@sing(…)` writes on the note: held from it ("sing"), or arrived at ("singTo").
                var settings = Semantics.AnnotationValues.SingOf(n.Sing);
                foreach (bool arrives in new[] { false, true })
                {
                    var some = settings.Where(s => s.Arrives == arrives).ToList();
                    if (some.Count == 0)
                        continue;
                    w.WriteStartObject(arrives ? "singTo" : "sing");
                    foreach (var s in some)
                    {
                        if (s.Number is { } number)
                            w.WriteNumber(s.Name, number);
                        else
                            w.WriteString(s.Name, s.Word);
                    }
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

/// <summary>A sung voice mixed into a rendered file: the sound, and where its first sample
/// falls in the score's seconds — <c>−SungScoreJson.Lead</c> for a WAV from the engine, which
/// begins that long before the score's 0 with the first consonant in the lead.</summary>
public sealed record VoiceTrack(string Part, MonoAudio Sound, double At = 0);
