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
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Audio;

/// <summary>
/// What is said when a singing engine FAILS on a voice it agreed to sing (owner, 2026-11-13,
/// kaze.lys: "alt is not sung (Failed to load phrase data); its instrument plays" named no
/// line, no cause and no place, and the editor showed the engine's words for eight seconds in
/// the status bar): which voice and singer line, what happened in plain words where the
/// engine's message is one Lily# knows, where to look when Lily# can tell, and the engine's
/// own message, as it gave it, at the end. LILYSHARP-OWN.
/// </summary>
/// <remarks>
/// ⚠️ TWO COPIES OF THE WORDING: <c>editors/vscode/src/singerCore.ts</c>
/// (<c>singerFailureCause</c>, <c>singerFailureText</c>) says the same sentences in the editor,
/// where the engine is the Cantoria extension and this assembly is not asked. A cause added
/// here is added there (<c>SingerFailureTests</c> and <c>test/singerFailure.test.ts</c> hold
/// the same table).
/// </remarks>
public static class SingerFailure
{
    /// <summary>
    /// The messages Lily# can put a plain cause to. Kept small: a message that is not here is
    /// shown as the engine said it. Each is a text the engine was SEEN or READ to give
    /// (Cantoria's <c>NeutrinoSinger</c>, <c>VoicevoxSinger</c>; NEUTRINO's own "Failed to load
    /// phrase data", which it answered to a phrase that began on a note with no syllable).
    /// </summary>
    private static readonly (string Pattern, string Cause)[] Causes =
    [
        ("Failed to load phrase data",
            "NEUTRINO could not make a phrase out of the notes it was given (seen when a phrase begins on a held note with no syllable, or a syllable is not one it has a sound for)"),
        (@"did not start|did not become ready|before it was ready",
            "the engine's program did not start"),
        (@"did not finish within|gave no answer in",
            "the engine took too long and was given up"),
        ("did not answer at",
            "the engine is not running"),
        // Cantoria's DiffSingerSinger.NoSoundFor (owner's report 2026-11-19, Moon-on-the-River.lys:
        // TIGER's dictionary spells 思 "s iz" and its pitch model has no "iz" - the warning named a
        // phoneme and a file of the voice, and no word of the song). $1 is the syllable.
        // FIRST the syllable whose phonemes the file wrote (`병[/pp.yeo/]`, session 1375): the
        // voice lacks a symbol the writer chose, and other words are not the remedy. $2 is the symbol.
        ("has no sound for \"([^\"]+)\" \\(note \\d+[^)]*\\): its phonemes are written \"[^\"]*\", and .*?has no phoneme \"([^\"]+)\"",
            "the phonemes written on the syllable \"$1\" are not all the voice's own: it has no \"$2\" - write its own symbols in the [/…/] there, or give the singer line another voice"),
        (UnsungPattern,
            "the voice has no sound for the syllable \"$1\" (its models were not taught a phoneme the syllable needs): write other words there, or give the singer line another voice"),
    ];

    /// <summary>What Cantoria says of a syllable a voice has no sound for: the syllable, and its
    /// note among the voice's notes, from 0.</summary>
    private const string UnsungPattern = "has no sound for \"([^\"]+)\" \\(note (\\d+)";

    /// <summary>The plain cause of <paramref name="engineMessage"/>, or null for a message Lily# does not know.
    /// A <c>$1</c> in a cause is what its pattern caught (the syllable), a <c>$2</c> its second catch.</summary>
    public static string? Cause(string? engineMessage)
    {
        if (string.IsNullOrWhiteSpace(engineMessage))
            return null;
        foreach (var (pattern, cause) in Causes)
            if (Regex.Match(engineMessage, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) is { Success: true } m)
                return cause.Replace("$1", m.Groups[1].Value, StringComparison.Ordinal).Replace("$2", m.Groups[2].Value, StringComparison.Ordinal);
        return null;
    }

    /// <summary>
    /// The source offset of the note under the syllable the engine says the voice has no sound
    /// for (<see cref="UnsungPattern"/>), or null: for another message, for a note the voice does
    /// not have, and where that note's syllable is not the one named (the engine was sent other
    /// notes than these).
    /// </summary>
    public static int? UnsungAt(string? engineMessage, Midi.SungVoice voice)
    {
        if (engineMessage == null
            || Regex.Match(engineMessage, UnsungPattern, RegexOptions.CultureInvariant) is not { Success: true } m
            || !int.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int note)
            || note >= voice.Notes.Count
            || voice.Notes[note].Lyric != m.Groups[1].Value)
            return null;
        return voice.Notes[note].SourcePos;
    }

    /// <summary>What Cantoria says, with a voice it SANG, of the syllables its dictionary has no
    /// entry for and it left silent (its <c>DiffSingerSinger.NotInDictionary</c>): `… does not sing
    /// "岸" (note 46 at 57.25 s), …`. The editor's <c>silentSyllables</c> reads the same words.</summary>
    public const string SilentPattern = " does not sing \"";

    /// <summary>
    /// The source offsets of the notes <paramref name="said"/> says the voice left silent, in its
    /// order; a note the voice does not have, or whose syllable is another, is left out. Empty
    /// for any other sentence.
    /// </summary>
    public static IReadOnlyList<int> SilentAt(string? said, Midi.SungVoice voice)
    {
        var found = new List<int>();
        if (said == null || !said.Contains(SilentPattern, StringComparison.Ordinal))
            return found;
        foreach (Match m in Regex.Matches(said, "\"([^\"]+)\" \\(note (\\d+) at", RegexOptions.CultureInvariant))
            if (int.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int note)
                && note < voice.Notes.Count && voice.Notes[note].Lyric == m.Groups[1].Value)
                found.Add(voice.Notes[note].SourcePos);
        return found;
    }

    /// <summary>
    /// The sentence: <paramref name="who"/> (the voice, with its singer line where known) is not
    /// sung, why in plain words when the message is known, where to look first
    /// (<paramref name="place"/>, or null), and the engine's message verbatim at the end.
    /// </summary>
    public static string Sentence(string who, string? engineMessage, string? place = null)
    {
        string said = string.IsNullOrWhiteSpace(engineMessage) ? "nothing" : engineMessage.Trim();
        string look = place == null ? "" : $" Look first at {place}.";
        return Cause(engineMessage) is { } cause
            ? $"{who} is not sung: {cause}. Its instrument plays instead.{look} The engine said: {said}"
            : $"{who} is not sung; its instrument plays instead.{look} The engine said: {said}";
    }

    /// <summary>
    /// Where a source offset is, for a reader of the file: its line, and the section and the bar
    /// of its block there, counted on the bar lines written (<c>R*4</c> is four) — "line 229
    /// (section Verse3, bar 11)". The bar is the WRITTEN one, not the page's number: a repeat
    /// and a pickup do not move it. Null for an offset outside <paramref name="text"/>.
    /// </summary>
    public static string? PlaceOf(SyntaxNode root, string text, int offset)
    {
        if (offset < 0 || offset >= text.Length)
            return null;
        int line = 1 + text.AsSpan(0, offset).Count('\n');
        var section = root.DescendantNodes<SectionDeclarationSyntax>()
            .Where(s => s.Span.Start <= offset && offset < s.Span.End)
            .OrderBy(s => s.Span.Length).FirstOrDefault();
        if (section == null)
            return $"line {line}";
        // The block the offset is in: back to the brace that opens it.
        int open = offset, depth = 0;
        while (open > section.Span.Start)
        {
            open--;
            if (text[open] == '}') depth++;
            else if (text[open] == '{' && depth-- == 0) break;
        }
        string before = Regex.Replace(text[open..offset], @"//[^\n]*", "").TrimStart('{', ' ', '\t', '\r', '\n');
        // One bar line a run (`||`, `|.`, `:|:`); a `|:` that opens the block ends no bar.
        int bar = 1 + Regex.Matches(before, @":?\|[|.:]*").Count(m => m.Index > 0)
            + Regex.Matches(before, @"\bR\*(\d+)").Sum(m => Math.Max(0, int.Parse(m.Groups[1].Value) - 1));
        return $"line {line} (section {section.SectionName}, bar {bar})";
    }

    /// <summary>A voice's own warning (<see cref="Midi.SungVoice.Warnings"/>) with where its
    /// first syllable stands (<see cref="Midi.SungVoice.WarningAt"/>).</summary>
    public static string WithPlace(string warning, string? place)
        => place == null ? warning : $"{warning} — the first at {place}";
}
