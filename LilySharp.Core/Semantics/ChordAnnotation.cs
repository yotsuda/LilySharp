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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// What an <c>@chord(…)</c> argument says, word by word: the chord symbol, and the chord
/// diagram it asks for — a voicing INDEX, a written POSITION STRING, and the strings to MUTE.
/// </summary>
/// <remarks>
/// <para>
/// Owner's decision, 2026-09-27 (index 0-based, 2026-09-28). The argument is read as WORDS —
/// the runs <see cref="MarkArgument"/> separates at whitespace and ',':
/// <code>
/// @chord(Cm7)                  name only
/// @chord(Cm7 2)                name + voicing 2 (counted from 0) of ChordVoicings
/// @chord(Cm7 2 mute 3 4)       + strings 3 and 4 muted (an overlay, never renumbered)
/// @chord(D mute 5)             voicing 0 with string 5 muted (the open D)
/// @chord(Cm7 x3x546)           name + a diagram written out
/// @chord(x32010)               a diagram written out; the name is derived from its notes
/// @chord                       bare: the name is derived from the notes it is on
/// </code>
/// Word 1 is a chord symbol (<see cref="ChordStructure.TryParseChordEntry"/>) or a position
/// string (it starts with <c>x</c>, <c>o</c> or a digit). Word 2, only after a symbol, is an
/// index (one to three digits) or a position string (as long as the tuning has strings, at
/// least four) — the two cannot be confused. <c>mute</c> takes one or more string numbers,
/// guitarist's numbering (1 = the highest-pitched string), in any order, and applies to the
/// index form only.
/// </para>
/// <para>
/// ⚠️ <b>A BREAKING CHANGE, accepted by the owner</b>: until 2026-09-27 the runs were
/// concatenated (<c>AnnotationValues.WrittenArgument</c>), so <c>@chord(C 7)</c> named C7. It
/// now names C with voicing 7. No book wrote a spaced symbol (measured: 0 over the repo's
/// <c>.lys</c> and the Lab corpora).
/// </para>
/// <para>
/// This type is the SYNTAX half and needs no tuning. The problems it can find without one are
/// in <see cref="Problem"/>; the ones a tuning decides (the index's range, the position
/// string's length, the <c>mute</c> numbers' range, a re-entrant tuning) are
/// <see cref="Resolve"/>'s. Every reader — the page, the validator, the twin, MusicXML, the
/// editor's hover — asks the same two, so none can read the words differently.
/// </para>
/// LILYSHARP-OWN: LilyPond names a chord in a ChordNames context and draws a diagram in a
/// FretBoards context; nothing in it chooses a diagram from a name.
/// </remarks>
public sealed record ChordAnnotation
{
    /// <summary>Word 1 when it is a chord symbol (as written), else null.</summary>
    public string? Symbol { get; init; }

    /// <summary>The chord <see cref="Symbol"/> names, or null (no symbol, or one that does
    /// not parse — the validator's unknown-annotation message covers that).</summary>
    public ChordStructure? Structure { get; init; }

    /// <summary>Word 1 when it is quoted free text (<c>@chord("N.C.")</c>), quotes removed.</summary>
    public string? QuotedText { get; init; }

    /// <summary>The written position string (word 1 or word 2), or null.</summary>
    public string? Written { get; init; }

    /// <summary>The explicit voicing index, or null (an index form without one means 0).</summary>
    public int? Index { get; init; }

    /// <summary>The strings <c>mute</c> lists, guitarist's numbering, as written.</summary>
    public ImmutableArray<int> Mutes { get; init; } = [];

    /// <summary>Whether <c>mute</c> was written at all.</summary>
    public bool HasMute { get; init; }

    /// <summary>The first problem the words have that no tuning can change, or null. When
    /// there is one no diagram is drawn; the name still is.</summary>
    public string? Problem { get; init; }

    /// <summary>No argument at all: the bare <c>@chord</c>, named from its notes.</summary>
    public bool IsBare { get; init; }

    /// <summary>Whether a diagram is asked for — an index, a position string, or <c>mute</c>
    /// (which implies index 0).</summary>
    public bool WantsDiagram => Written != null || Index != null || HasMute;

    /// <summary>The index form: a symbol with an index or a <c>mute</c>.</summary>
    public bool IsIndexForm => Written == null && (Index != null || HasMute);

    /// <summary>A diagram with no symbol: the name comes from the diagram's notes.</summary>
    public bool NamesFromDiagram => Symbol == null && QuotedText == null && Written != null;

    /// <summary>The word that asks for the strings to be muted.</summary>
    public const string MuteWord = "mute";

    /// <summary>The longest index: one to three digits (a position string is at least four
    /// characters, so the two never meet).</summary>
    public const int MaxIndexDigits = 3;

    /// <summary>Reads the words of <paramref name="mark"/>, or null when it is not an
    /// <c>@chord</c>.</summary>
    public static ChordAnnotation? Of(MusicMarkSyntax mark)
        => string.Equals(mark.Name, "chord", System.StringComparison.Ordinal)
            ? Parse(mark.Arguments.Select(a => a.Text).ToList())
            : null;

    /// <summary>Reads a list of words (the runs of the argument).</summary>
    public static ChordAnnotation Parse(IReadOnlyList<string> words)
    {
        if (words.Count == 0)
            return new ChordAnnotation { IsBare = true };

        string first = words[0];
        // Quoted free text prints verbatim, as it always has; nothing may follow it.
        if (first.Length > 0 && first[0] == '"')
        {
            int close = first.LastIndexOf('"');
            return new ChordAnnotation
            {
                QuotedText = close >= 1 ? first.Substring(1, close - 1) : null,
                Problem = words.Count > 1 ? ExtraWords(words[1], "quoted text") : null,
            };
        }

        // A position string alone: the diagram is written out, the name comes from it.
        if (StartsPositionString(first))
        {
            string? problem = IsPositionString(first) ? null : NotAPositionString(first);
            if (problem == null && words.Count > 1)
                problem = string.Equals(words[1], MuteWord, System.StringComparison.Ordinal)
                    ? MuteAfterWritten(first)
                    : ExtraWords(words[1], "a written-out diagram");
            return new ChordAnnotation { Written = first, Problem = problem };
        }

        // A chord symbol, then its diagram.
        var result = new ChordAnnotation
        {
            Symbol = first,
            Structure = ChordStructure.TryParseChordEntry(first, out var parsed) ? parsed : null,
        };
        int i = 1;
        if (i < words.Count && !string.Equals(words[i], MuteWord, System.StringComparison.Ordinal))
        {
            string w = words[i];
            if (IsIndexWord(w))
                result = result with { Index = int.Parse(w, System.Globalization.CultureInfo.InvariantCulture) };
            else if (StartsPositionString(w) && IsPositionString(w))
                result = result with { Written = w };
            else
                return result with { Problem = NeitherIndexNorPosition(w) };
            i++;
        }
        if (i < words.Count && string.Equals(words[i], MuteWord, System.StringComparison.Ordinal))
        {
            if (result.Written != null)
                return result with { Problem = MuteAfterWritten(result.Written) };
            i++;
            var mutes = ImmutableArray.CreateBuilder<int>();
            while (i < words.Count && words[i].Length > 0 && words[i].All(char.IsAsciiDigit))
            {
                int s = int.Parse(words[i], System.Globalization.CultureInfo.InvariantCulture);
                if (mutes.Contains(s))
                    return result with { HasMute = true, Problem = MuteTwice(s) };
                mutes.Add(s);
                i++;
            }
            result = result with { HasMute = true, Mutes = mutes.ToImmutable() };
            if (mutes.Count == 0)
                return result with { Problem = MuteWithoutNumbers() };
        }
        if (i < words.Count)
            return result with
            {
                Problem = ExtraWords(words[i],
                    result.HasMute ? "the muted strings"
                    : result.Written != null ? "a written-out diagram"
                    : result.Index != null ? "the voicing index"
                    : "the chord symbol"),
            };
        return result;
    }

    /// <summary>An index word: one to three ASCII digits.</summary>
    private static bool IsIndexWord(string w)
        => w.Length is >= 1 and <= MaxIndexDigits && w.All(char.IsAsciiDigit);

    /// <summary>A word that sets out to be a position string: it starts with x, o or a digit
    /// (lower case — a chord symbol starts with a capital A–G).</summary>
    public static bool StartsPositionString(string w)
        => w.Length > 0 && (w[0] is 'x' or 'o' || char.IsAsciiDigit(w[0]));

    /// <summary>One of x, o, 0–9 per string (the alphabet <c>@diagram</c> takes).</summary>
    public static bool IsPositionString(string w)
        => w.Length > 0 && w.All(ch => ch is 'x' or 'o' || char.IsAsciiDigit(ch));

    // ---------------------------------------------------------------- resolution

    /// <summary>What a tuning makes of the words: the diagram's frets (LOW string first, −1
    /// muted), which voicing it is and how many there were, or the problem that stopped it.</summary>
    public readonly record struct Resolution(
        ImmutableArray<int> Frets, int Index, int Count, string? Problem)
    {
        /// <summary>A diagram is drawn.</summary>
        public bool HasDiagram => !Frets.IsDefaultOrEmpty;
    }

    /// <summary>
    /// The diagram these words draw on <paramref name="tuning"/> (open strings as MIDI numbers,
    /// low string first), or the reason there is none. A request with no diagram, or with a
    /// <see cref="Problem"/> already, resolves to nothing and names nothing new.
    /// </summary>
    public Resolution Resolve(IReadOnlyList<int> tuning)
    {
        if (!WantsDiagram || Problem != null)
            return new Resolution(default, -1, 0, null);
        int n = tuning.Count;

        if (Written != null)
        {
            if (Written.Length != n)
                return new Resolution(default, -1, 0, WrongLength(Written, n));
            var written = ImmutableArray.CreateBuilder<int>(n);
            foreach (char ch in Written)
                written.Add(ch switch { 'x' => -1, 'o' => 0, _ => ch - '0' });
            return new Resolution(written.MoveToImmutable(), -1, 0, null);
        }

        // The index form.
        if (Structure is not { } chord)
            return new Resolution(default, -1, 0, null);   // the unknown symbol is reported already
        if (!ChordVoicings.IsGuitarType(tuning))
            return new Resolution(default, -1, 0, NotGuitarType(Symbol!, n));
        var set = ChordVoicings.For(tuning, chord);
        int count = set.Bases.Length;
        if (count == 0)
            return new Resolution(default, -1, 0, NoVoicing(Symbol!, n));
        int index = Index ?? 0;
        if (index >= count)
            return new Resolution(default, index, count, OutOfRange(Symbol!, count));
        foreach (int s in Mutes)
            if (s < 1 || s > n)
                return new Resolution(default, index, count, NoSuchString(s, n));
        var frets = set.Bases[index].ToBuilder();
        // The overlay: string s (1 = the highest-pitched, the LAST in the list) is muted.
        // Never re-validated and never renumbered — the owner's decision.
        foreach (int s in Mutes)
            frets[n - s] = -1;
        return new Resolution(frets.ToImmutable(), index, count, null);
    }

    /// <summary>
    /// The tuning of the part <paramref name="node"/> is written in (its <c>tuning</c>, else
    /// its preset's, else the guitar — <see cref="PartHeaderDefaults.Tuning"/>), found from the
    /// SYNTAX: the enclosing <c>part</c> declaration, or the part a section's block names. Null
    /// when the node is outside every part (a phrase, a top-level block) and the parts of the
    /// file are not all tuned alike — then only the page, which knows which part plays it,
    /// can tell.
    /// </summary>
    /// <remarks>
    /// The page does not ask this: it knows the part it is collecting
    /// (<c>MeasureCollector</c>'s part tuning). The validator, the twin and the editor's
    /// hover, which read the tree, do.
    /// </remarks>
    public static IReadOnlyList<int>? PartTuningOf(SyntaxNode node)
    {
        SyntaxNode root = node;
        while (root.Parent != null)
            root = root.Parent;
        for (var p = node.Parent; p != null; p = p.Parent)
        {
            if (p is PartDeclarationSyntax declared)
                return Tablature.Tunings.GetTuning(PartHeaderDefaults.Read(declared).Tuning);
            if (p is PartBlockSyntax block)
                return Tablature.Tunings.GetTuning(
                    PartHeaderDefaults.Read(ConcertPitch.FindPart(root, block.Name)).Tuning);
        }
        var tunings = root.ChildNodes().OfType<PartDeclarationSyntax>()
            .Select(pd => PartHeaderDefaults.Read(pd).Tuning).Distinct().ToList();
        return tunings.Count switch
        {
            0 => Tablature.Tunings.Guitar,
            1 => Tablature.Tunings.GetTuning(tunings[0]),
            _ => null,
        };
    }

    /// <summary>
    /// The chord a written-out diagram sounds, named by the recognizer a bare <c>@chord</c>
    /// uses on notes (<see cref="ChordStructure.TryRecognize(IReadOnlyList{ValueTuple{int, int}}, out ChordStructure?)"/>),
    /// its notes spelled in the key of <paramref name="keySharps"/>; null when they name none.
    /// </summary>
    /// <remarks>
    /// ⚠️ The SPELLING is Lily#'s choice (the frets say pitch, not letter): a pitch class the key
    /// has on one of its seven letters takes that letter, any other a sharp in a sharp key or
    /// C major and a flat in a flat key. It decides only how the name PRINTS (C♯ or D♭); which
    /// chord is named depends on the pitch classes alone.
    /// </remarks>
    public static ChordStructure? NameFromFrets(
        IReadOnlyList<int> frets, IReadOnlyList<int> tuning, int keySharps)
    {
        var pitches = new List<int>();
        for (int i = 0; i < frets.Count && i < tuning.Count; i++)
            if (frets[i] >= 0)
                pitches.Add(tuning[i] + frets[i]);
        if (pitches.Count == 0)
            return null;
        pitches.Sort();
        var members = pitches.Select(p => SpellPitchClass(((p % 12) + 12) % 12, keySharps)).ToList();
        return ChordStructure.TryRecognize(members, out var structure) ? structure : null;
    }

    /// <summary>A pitch class as (letter step, alteration) in the key of <paramref name="keySharps"/>.</summary>
    internal static (int Step, int Alter) SpellPitchClass(int pc, int keySharps)
    {
        for (int step = 0; step < 7; step++)
        {
            int alter = KeySpelling.Alteration(step, keySharps);
            if ((((RelativeOctave.StepSemitoneOf(step) + alter) % 12) + 12) % 12 == pc)
                return (step, alter);
        }
        bool flats = keySharps < 0;
        for (int step = 0; step < 7; step++)
        {
            int natural = RelativeOctave.StepSemitoneOf(step);
            if (!flats && (natural + 1) % 12 == pc)
                return (step, 1);
            if (flats && ((natural - 1) % 12 + 12) % 12 == pc)
                return (step, -1);
        }
        return (0, 0);   // unreachable: every pitch class is a natural or a neighbour's sharp/flat
    }

    // ---------------------------------------------------------------- the messages
    // Each names the fix. Shared by the validator (which reports them) and the tests.

    internal static string NeitherIndexNorPosition(string w)
        => $"'{w}' after the chord symbol is neither a voicing index (0, 1, 2 …) nor a position "
           + "string such as 'x32010' — no diagram is drawn.";

    internal static string NotAPositionString(string w)
        => $"'{w}' is not a position string: write one character per string, low string first — "
           + "'x' muted, 'o' or '0' open, a digit for the fret (x32010). No diagram is drawn.";

    internal static string ExtraWords(string w, string after)
        => $"'{w}' is not understood after {after} — no diagram is drawn. The forms are "
           + "@chord(Cm7 2), @chord(Cm7 2 mute 3 4), @chord(Cm7 x3x546) and @chord(x32010).";

    internal static string MuteWithoutNumbers()
        => "'mute' needs the strings to mute, e.g. @chord(D mute 5) — string 1 is the "
           + "highest-pitched. No diagram is drawn.";

    internal static string MuteTwice(int s)
        => $"string {s} is listed twice after 'mute' — list each string once. No diagram is drawn.";

    internal static string MuteAfterWritten(string written)
        => $"'mute' applies to a voicing chosen by index; a written-out diagram mutes with its own "
           + $"'x' — write the x into '{written}'. No diagram is drawn.";

    internal static string WrongLength(string written, int strings)
        => $"'{written}' has {written.Length} characters but this part's tuning has {strings} "
           + "strings — a position string is one character per string, low string first. "
           + "No diagram is drawn.";

    internal static string NotGuitarType(string symbol, int strings)
        => $"a voicing index needs a tuning whose strings rise in pitch from the lowest string to "
           + $"the highest, and this part's does not (a re-entrant tuning, as a ukulele's) — "
           + $"write the diagram out instead: @chord({symbol} {new string('x', strings)}) with "
           + "the frets filled in. No diagram is drawn.";

    internal static string NoVoicing(string symbol, int strings)
        => $"{symbol} has no voicing on this tuning under Lily#'s voicing rules — write the "
           + $"diagram out instead: @chord({symbol} {new string('x', strings)}) with the frets "
           + "filled in. No diagram is drawn.";

    internal static string OutOfRange(string symbol, int count)
        => $"{symbol} has {count} voicings on this tuning (0–{count - 1}). No diagram is drawn.";

    internal static string NoSuchString(int s, int strings)
        => $"string {s} does not exist on this tuning: 'mute' takes 1–{strings}, 1 being the "
           + "highest-pitched string. No diagram is drawn.";

    internal static string NoDerivedName(string written)
        => $"the notes of '{written}' name no chord Lily# knows, so no chord name is drawn — "
           + $"write the name first: @chord(C {written}).";
}
