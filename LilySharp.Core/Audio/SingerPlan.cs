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
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using LilySharp.Core.Midi;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Audio;

/// <summary>
/// What one sung voice asks a singing engine for: the voice its <c>singer</c> line names
/// (<c>neutrino/MERROW</c>), or a gender alone, in the language of its words — one entry of the
/// <c>asked</c> list <c>cantoria plan</c> reads (Cantoria's docs/design.md §4.3).
/// </summary>
/// <param name="Part">The part the voice sings.</param>
/// <param name="Key">What tells the voice from the others of its part (<see cref="SungVoice.Key"/>).</param>
/// <param name="Voice">The voice named, or null: the gender's default.</param>
/// <param name="Gender">The gender of the default voice for it.</param>
/// <param name="Language">The tag of the language its words are in.</param>
/// <param name="Position">Where its singer line stands in the file's own text (the <c>singer</c>
/// word), or -1 where no line of the file is found for it.</param>
/// <param name="Text">That line as written, on one line; null with no line.</param>
public sealed record SingerAsk(string Part, string Key, string? Voice, string? Gender, string Language,
    int Position = -1, string? Text = null);

/// <summary>
/// The engine's answer for one <see cref="SingerAsk"/>: the installed voice that would sing it
/// (<c>engine/name</c>, what <c>cantoria sing --voice</c> takes), with a <paramref name="Note"/>
/// when that is not the one asked for; or no voice, and the <paramref name="Reason"/> in words
/// for whoever wrote the score.
/// </summary>
public sealed record PlannedSinger(SingerAsk Asked, string? Voice, string? Note, string? Reason);

/// <summary>
/// Who would sing a score ON THIS MACHINE, asked of the singing engine before anything is sung
/// (owner's decisions 2026-11-13). A piece in Latin played with no voice and no instrument on
/// its alto, and neither <c>lysc check</c> nor the editor said why: no installed voice sings
/// Latin. The compiler stays ignorant of what is installed — its plain output is the same on
/// every machine — so the question goes to Cantoria, which knows (<c>cantoria plan</c>), and
/// only where the user names it: <c>lysc check --singer</c>, <c>lysc wav --singer</c>.
/// LILYSHARP-OWN.
/// </summary>
/// <remarks>
/// The asked list is built from the SUNG VOICES (<see cref="SungVoices.Read"/>), one entry a
/// voice, so what is asked is what the playback would sing: a harmony line is a voice of its
/// own, a singer printed as a staff is asked once, a part in two languages twice. The answer is
/// read in the order asked. A line with no singer is not an error of the file: its part's
/// instrument plays (<see cref="SungVoices.Unsung"/> takes the voices that have one).
/// </remarks>
public sealed class SingerPlan
{
    /// <summary>The answers, one for each voice asked, in the order asked.</summary>
    public IReadOnlyList<PlannedSinger> Singers { get; }

    private SingerPlan(IReadOnlyList<PlannedSinger> singers) => Singers = singers;

    /// <summary>The answer for <paramref name="voice"/> (by its part and key), or null where
    /// it was not asked.</summary>
    public PlannedSinger? Of(SungVoice voice)
        => Singers.FirstOrDefault(s => s.Asked.Part == voice.Part && s.Asked.Key == (voice.Key ?? voice.Part));

    /// <summary>
    /// What <paramref name="voices"/> ask for. A voice whose line names a voice asks for it; one
    /// that says a gender, or nothing, asks for that gender's default — or for
    /// <paramref name="defaultVoice"/>, the voice the command line names (<c>--voice</c>), which
    /// then sings every line that names none, as it did. <paramref name="lineOf"/> finds a
    /// voice's singer line (<see cref="Music.LyricSinger.LineReaderOf"/>), for the place a message
    /// stands at; <paramref name="ownTextEnd"/> is where the file's own text ends
    /// (<see cref="Editing.SingerParts.MadeFrom"/>): a line past it is not the writer's.
    /// </summary>
    public static IReadOnlyList<SingerAsk> AskedOf(IEnumerable<SungVoice> voices, string? defaultVoice = null,
        Func<SungVoice, SingerDeclarationSyntax?>? lineOf = null, string? text = null, int ownTextEnd = int.MaxValue)
    {
        var asked = new List<SingerAsk>();
        foreach (var voice in voices)
        {
            string? named = voice.Singer is null or "male" or "female" ? null : voice.Singer;
            int position = -1;
            string? written = null;
            if (lineOf?.Invoke(voice) is { } line && line.Keyword.Span.Start < ownTextEnd)
            {
                position = line.Keyword.Span.Start;
                if (text != null)
                {
                    // (A token's span may begin at the blank before it.)
                    while (position < text.Length && char.IsWhiteSpace(text[position]))
                        position++;
                    int end = Math.Min(text.Length, line.Span.End);
                    written = string.Join(' ', text[position..Math.Max(position, end)]
                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                }
            }
            asked.Add(new SingerAsk(voice.Part, voice.Key ?? voice.Part, named ?? (string.IsNullOrWhiteSpace(defaultVoice) ? null : defaultVoice),
                voice.Gender, voice.Language, position, written));
        }
        return asked;
    }

    /// <summary>
    /// What one score of <paramref name="tree"/> asks for: its sung voices as the WAV export
    /// sings them (<see cref="Export.ScoreExport.SungVoicesOf"/>), each with its singer line.
    /// <paramref name="midi"/> is the score's export when the caller holds it.
    /// </summary>
    public static IReadOnlyList<SingerAsk> AskedOfScore(SyntaxTree tree, RenderSpec? spec, string? defaultVoice = null, MidiFile? midi = null)
    {
        midi ??= new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
        var shown = spec ?? RenderSpecParser.PlayedSpec(tree, null, null);
        var lineOf = Music.LyricSinger.LineReaderOf(tree.GetRoot(), shown);
        return AskedOf(Export.ScoreExport.SungVoicesOf(tree, spec, midi), defaultVoice,
            v => lineOf(v.Track, v.Part, v.Line), tree.Text, Editing.SingerParts.MadeFrom(tree.Text));
    }

    private static readonly JsonWriterOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    /// <summary>The asked list as the file <c>cantoria plan</c> reads:
    /// <c>{"asked":[{"part":…,"key":…,"voice"?:…,"gender"?:…,"language":…}]}</c>.</summary>
    public static string AskedJson(IReadOnlyList<SingerAsk> asked)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, Options))
        {
            w.WriteStartObject();
            w.WriteStartArray("asked");
            foreach (var ask in asked)
            {
                w.WriteStartObject();
                w.WriteString("part", ask.Part);
                w.WriteString("key", ask.Key);
                if (ask.Voice != null)
                    w.WriteString("voice", ask.Voice);
                if (ask.Gender != null)
                    w.WriteString("gender", ask.Gender);
                w.WriteString("language", ask.Language);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// The plan <c>cantoria plan --json</c> printed for <paramref name="asked"/>:
    /// <c>{"ready":…,"singers":[{"voice":"engine/name"|null,"note"?:…,"reason":…|null}]}</c>, an
    /// answer for each voice asked, in the order asked.
    /// </summary>
    /// <exception cref="InvalidDataException">The text is not such an answer.</exception>
    public static SingerPlan Read(string json, IReadOnlyList<SingerAsk> asked)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("singers", out var singers) || singers.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("the answer lists no singers");
            if (singers.GetArrayLength() != asked.Count)
                throw new InvalidDataException($"the answer is for {singers.GetArrayLength()} voice(s), and {asked.Count} were asked");
            static string? Text(JsonElement of, string name)
                => of.ValueKind == JsonValueKind.Object && of.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } text ? text : null;
            var planned = new List<PlannedSinger>(asked.Count);
            int i = 0;
            foreach (var singer in singers.EnumerateArray())
            {
                string? voice = Text(singer, "voice");
                planned.Add(new PlannedSinger(asked[i++], voice, Text(singer, "note"),
                    voice == null ? Text(singer, "reason") ?? "no installed voice sings it" : null));
            }
            return new SingerPlan(planned);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("the answer is not JSON: " + e.Message, e);
        }
    }

    /// <summary>
    /// Asks <paramref name="engine"/> who would sing <paramref name="asked"/>. Null and no
    /// problem for an engine that has no such answer (a test's stand-in): the caller then does
    /// what it did before there was a plan. A problem is one sentence — the program is missing,
    /// too old to know <c>plan</c>, or failed — and no plan.
    /// </summary>
    public static (SingerPlan? Plan, string? Problem) Ask(SingingEngine engine, IReadOnlyList<SingerAsk> asked)
    {
        var (json, problem) = engine.Plan(AskedJson(asked));
        if (problem != null || json == null)
            return (null, problem);
        try
        {
            return (Read(json, asked), null);
        }
        catch (InvalidDataException e)
        {
            return (null, $"{engine.Name} did not say who would sing: {e.Message}");
        }
    }

    /// <summary>
    /// The one sentence for a singer line nobody installed can sing, at every door (<c>lysc
    /// check --singer</c>, <c>lysc wav --singer</c>; the editor's is its own, in its words):
    /// Cantoria's reason, and what plays then.
    /// </summary>
    public static string NotSung(string? reason)
        => $"No installed voice can sing this line: {(reason ?? "no installed voice sings it").Trim().TrimEnd('.')}. The part's instrument plays instead.";
}
