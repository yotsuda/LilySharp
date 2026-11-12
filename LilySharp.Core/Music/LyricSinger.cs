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
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Music;

/// <summary>
/// Who sings what: the file's <c>singer [S] sings [NAME] [on PART]</c> lines
/// (docs/lyrics-singer-design.md). Lily# only carries the singer's string: what sings is the
/// engine's to resolve (docs/singing-roadmap.md §4), the listener's settings having the last say.
/// </summary>
public static class LyricSinger
{
    /// <summary>The name of the lyrics track <see cref="Editing.SingerParts"/> writes a named
    /// singer's words under: no file writes such a name.</summary>
    public static string WordsOf(string person) => "__words_" + person;

    /// <summary>
    /// The lines of the singer <paramref name="person"/> as <paramref name="score"/> reads them
    /// (owner's report 2026-11-12: a Japanese score with its own singer lines printed alice with
    /// the English words — her staff was made of every line of her name, whatever score wrote it):
    /// the score's own, and the top level's for every part the score writes no line for — the
    /// rule <see cref="LinesFor"/> sings by.
    /// </summary>
    public static IReadOnlyList<Line> LinesOfSingerIn(SyntaxNode root, string person, RenderDeclarationSyntax? score)
    {
        var lines = LinesOf(root);
        int at = score?.Span.Start ?? -1;
        var own = lines.Where(l => at >= 0 && l.Score == at).ToList();
        var replaced = own.Select(l => l.Part).ToHashSet();
        return [.. lines.Where(l => l.Score < 0 && !replaced.Contains(l.Part) && l.Person == person), .. own.Where(l => l.Person == person)];
    }

    /// <summary>
    /// The name of the part a score's <c>staff NAME</c> item is the staff of: the singer's name
    /// where the score reads her lines as the file's top level writes them, and a name of the
    /// score's own (<c>NAME__2</c>: the second score) where its own singer lines say what she —
    /// or her part — sings there. So one singer has one staff for the English score and another
    /// for the Japanese. Read off the syntax alone: the resolved lines read the scores
    /// (<see cref="LinesOf"/>), and a score's items are read by this.
    /// </summary>
    public static string PartOfItem(StaffRenderSyntax item) => PartOfSinger(item, item.PartToken?.Text ?? "");

    private sealed record SingerNameSets(HashSet<string> Staves, HashSet<string> Words);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SyntaxNode, SingerNameSets> Names = new();

    private static SingerNameSets NamesOf(SyntaxNode node)
    {
        while (node.Parent != null)
            node = node.Parent;
        return Names.GetValue(node, static root =>
        {
            var persons = root.SitesOfKinds(SyntaxKind.SingerDeclaration).OfType<SingerDeclarationSyntax>()
                .Select(l => l.PersonToken?.Text).OfType<string>().ToHashSet(StringComparer.Ordinal);
            var staves = new HashSet<string>(persons, StringComparer.Ordinal);
            var words = new HashSet<string>(persons, StringComparer.Ordinal);
            if (persons.Count > 0)
            {
                foreach (var n in root.SitesOfKinds(Editing.PartReferenceFinder.DeclaringKinds))
                    if (Editing.PartReferenceFinder.DeclaredName(n) is { } declared)
                        staves.Remove(declared.Text);
                foreach (var block in root.SitesOfKinds(SyntaxKind.LyricsBlock).OfType<LyricsBlockSyntax>())
                    if (block.VoiceName is { } track)
                        words.Remove(track);
            }
            return new SingerNameSets(staves, words);
        });
    }

    /// <summary>
    /// The names a score's <c>staff NAME</c> reads as a SINGER (owner's decision 2026-11-12:
    /// <c>staff carol</c> is the staff of what the singer carol sings, <c>lyrics carol</c> her
    /// words — a score prints what it says, of a singer as of a part): the names the singer
    /// lines give, but for those a part is called. A part of that name is the part — which is
    /// what a singer's notes written out are.
    /// </summary>
    public static IReadOnlySet<string> SingerNames(SyntaxNode node) => NamesOf(node).Staves;

    /// <summary>The names a score's <c>lyrics NAME</c> reads as a singer's words: the singers'
    /// names, but for those a lyrics track is called.</summary>
    public static IReadOnlySet<string> SingerWordNames(SyntaxNode node) => NamesOf(node).Words;

    /// <summary>The part made of the singer <paramref name="person"/>'s lines for the score
    /// <paramref name="item"/> stands in (<see cref="PartOfItem"/>).</summary>
    public static string PartOfSinger(SyntaxNode item, string person)
    {
        SyntaxNode root = item;
        RenderDeclarationSyntax? score = null;
        while (root.Parent != null)
        {
            score ??= root as RenderDeclarationSyntax;
            root = root.Parent;
        }
        // ⚠️ Never the singer's bare name: a part called as she is is HER NOTES WRITTEN OUT, and
        // `staff carol` then means that part (SingerNames). A made part of that name would turn
        // the item that asked for it into a staff of a part — in the very text that makes it.
        if (score == null)
            return person + ScopedMark + "0";
        var own = score.ChildNodes().OfType<SingerDeclarationSyntax>().ToList();
        var replaced = own.Select(l => l.OnToken?.Text).ToHashSet();
        bool scoped = own.Any(l => l.PersonToken?.Text == person)
            || root.ChildNodes().OfType<SingerDeclarationSyntax>().Any(l => l.PersonToken?.Text == person && replaced.Contains(l.OnToken?.Text));
        if (!scoped)
            return person + ScopedMark + "0";
        int index = root.ChildNodes().OfType<RenderDeclarationSyntax>().TakeWhile(r => r.Span.Start != score.Span.Start).Count() + 1;
        return person + ScopedMark + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The track a score's <c>lyrics NAME</c> places for the singer <paramref name="person"/>:
    /// the words written for her staff (<see cref="WordsOf"/> of <see cref="PartOfSinger"/>), or
    /// — where her lines sing words made from the notes (<c>sings solfege</c>) — those, which no
    /// track holds. Her lines are the score's own where it writes any for her, else the file's;
    /// read off the syntax alone, as <see cref="PartOfSinger"/> is.
    /// </summary>
    public static string WordsOfSingerAt(SyntaxNode item, string person)
    {
        SyntaxNode root = item;
        RenderDeclarationSyntax? score = null;
        while (root.Parent != null)
        {
            score ??= root as RenderDeclarationSyntax;
            root = root.Parent;
        }
        var lines = score?.ChildNodes().OfType<SingerDeclarationSyntax>().Where(l => l.PersonToken?.Text == person).ToList() ?? [];
        if (lines.Count == 0)
            lines = root.ChildNodes().OfType<SingerDeclarationSyntax>().Where(l => l.PersonToken?.Text == person).ToList();
        return lines.Select(l => l.TrackToken?.Text).FirstOrDefault(t => t != null && GeneratedLyrics.IsGenerated(t))
            ?? WordsOf(PartOfSinger(item, person));
    }

    /// <summary>Whether <paramref name="part"/> is a part made of a singer's lines
    /// (<see cref="PartOfSinger"/>: her name, the mark, a number) and not one the file writes.</summary>
    public static bool IsMadePart(SyntaxNode node, string part)
    {
        int at = part.LastIndexOf(ScopedMark, StringComparison.Ordinal);
        if (at <= 0 || at + ScopedMark.Length >= part.Length || !part[(at + ScopedMark.Length)..].All(char.IsAsciiDigit))
            return false;
        while (node.Parent != null)
            node = node.Parent;
        string person = part[..at];
        return node.SitesOfKinds(SyntaxKind.SingerDeclaration).OfType<SingerDeclarationSyntax>().Any(l => l.PersonToken?.Text == person);
    }

    /// <summary>What stands between a singer's name and a score's number in the name of a part
    /// made for that score alone (<see cref="PartOfItem"/>).</summary>
    public const string ScopedMark = "__";

    /// <summary>The word that names a singer. Contextual: a part or a track may be called it.</summary>
    public const string Keyword = "singer";

    /// <summary>The words that may stand between <c>singer</c> and its name (<c>singer male "neutrino/SOMA"</c>,
    /// owner's decisions 2026-11-09, sessions 1173 and 1175): which voices the editor offers for it, and,
    /// written with no name, the singer itself (carried as the string <c>male</c>, for the engine to pick a
    /// voice of that gender). Lower-case, closed.</summary>
    public static readonly IReadOnlyList<string> Genders = ["male", "female"];

    /// <summary>The track name of the file's unnamed lyrics, for the language server's reading of a head.</summary>
    public const string UnnamedTrack = SyntaxFacts.UnnamedLyricsName;

    /// <summary>What joins, in a part's one lyric event, the voices of several <c>singer</c> lines on
    /// its words; <see cref="Midi.SungVoices"/> makes one voice of each again. A line that names no
    /// voice is the empty piece.</summary>
    public const char Joiner = '+';

    /// <summary>The text between a string token's quotes.</summary>
    public static string ValueOf(string token)
        => StringLiteral.IsClosed(token) ? StringLiteral.Value(token) : token.Trim('"');

    /// <summary>
    /// A reader of whether a part is sung by a male or a female voice when no voice is named, for the
    /// engine's default voice of that gender (owner's decision 2026-11-09, session 1187): what the
    /// singer's word says (<c>male</c>, <c>soprano</c>), else the part's preset, else its clef, a
    /// clef for male voices being bass, baritone, tenor or the treble an octave down.
    /// </summary>
    public static Func<string?, string?, string> GenderReaderOf(SyntaxNode root)
    {
        var parts = new Dictionary<string, Semantics.PartHeaderDefaults>(StringComparer.Ordinal);
        foreach (var part in root.DescendantNodes<PartDeclarationSyntax>())
            parts.TryAdd(part.Name.Text, Semantics.PartHeaderDefaults.Read(part));
        return (singer, part) => GenderOfWord(singer)
            ?? (part != null && parts.TryGetValue(part, out var header) ? GenderOfPart(header.Preset, header.ClefWord) : Female);
    }

    private const string Female = "female", Male = "male";

    private static string? GenderOfWord(string? word) => word switch { Female => Female, Male => Male, _ => null };

    private static string? GenderOfPreset(string? preset) => preset switch
    {
        "soprano" or "mezzosoprano" or "alto" or "contralto" => Female,
        "tenor" or "baritone" or "bass" => Male,
        _ => null,
    };

    private static string GenderOfPart(string? preset, string? clef)
        => GenderOfPreset(preset?.StartsWith("voice-", StringComparison.Ordinal) == true ? preset["voice-".Length..] : preset)
            ?? (clef is "bass" or "bass_8" or "baritone" or "tenor" or "treble_8" ? Male : Female);

    /// <summary>One <c>singer</c> line, resolved: who (null for the part's default), which track,
    /// the part whose notes it sings (null when nothing says: an error), the voice of that
    /// part it names (null when it names the part), and the source start of the score it is written
    /// in (<see cref="RenderSpec.SourceStart"/>; -1 at the top level, where it is every score's default).</summary>
    /// <paramref name="Person"/> is the singer's own name (<c>singer carol …</c>), or null;
    /// <paramref name="Printed"/> says the line is read in a score that prints that singer
    /// (<c>score { staff carol  lyrics carol }</c>): its part is then the singer's own, made of the lines
    /// (<see cref="Editing.SingerParts"/>), and what the line says of its place is in those notes already.
    public sealed record Line(string? Singer, string Track, string? Part, string? Voice, SingerDeclarationSyntax Node, int Score = -1,
        string? Person = null, bool Printed = false);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SyntaxNode, IReadOnlyList<Line>> Lines = new();

    /// <summary>The file's <c>singer</c> lines, wherever written, each resolved (§4 of the design: its
    /// <c>on</c>, the file's only part, or the one part every score of its scope prints the words under).</summary>
    public static IReadOnlyList<Line> LinesOf(SyntaxNode root)
    {
        while (root.Parent != null)
            root = root.Parent;
        return Lines.GetValue(root, BuildLines);
    }

    /// <summary>The lines that sing in <paramref name="spec"/>: its own, and the top level's for every
    /// part it writes none for (owner's decision 2026-11-10).</summary>
    public static IReadOnlyList<Line> LinesFor(SyntaxNode root, RenderSpec? spec)
    {
        var lines = LinesOf(root);
        if (lines.Any(l => l.Score >= 0))
        {
            var own = lines.Where(l => spec != null && spec.SourceStart >= 0 && l.Score == spec.SourceStart).ToList();
            var replaced = own.Select(l => l.Part).ToHashSet();
            lines = [.. lines.Where(l => l.Score < 0 && !replaced.Contains(l.Part)), .. own];
        }
        return OnPrintedParts(root, spec, lines);
    }

    /// <summary>
    /// <paramref name="lines"/> with those of a singer the score PRINTS (<c>staff carol  lyrics carol</c>)
    /// moved onto that singer's own part: the score sings what it shows, the notes of the
    /// printed staff, and not the line's part moved once more. Several lines of one singer
    /// become one voice there. A score that does not print the singer sings the line as written.
    /// </summary>
    private static IReadOnlyList<Line> OnPrintedParts(SyntaxNode root, RenderSpec? spec, IReadOnlyList<Line> lines)
    {
        if (spec == null || !lines.Any(l => l.Person != null))
            return lines;
        while (root.Parent != null)
            root = root.Parent;
        var made = root.ChildNodes().OfType<PartDeclarationSyntax>().Select(p => p.Name.Text).ToHashSet(StringComparer.Ordinal);
        var printed = spec.GetVoiceBindings().Select(b => b.VoiceName).Where(made.Contains).ToHashSet(StringComparer.Ordinal);
        // The staff the score prints for a singer: called as she is, or the score's own (PartOfItem).
        string? StaffOf(string person)
            => printed.FirstOrDefault(n => n.StartsWith(person + ScopedMark, StringComparison.Ordinal));
        if (!lines.Any(l => l.Person != null && StaffOf(l.Person) != null))
            return lines;
        return lines.Select(l => l.Person is { } person && StaffOf(person) is { } staff
            ? l with
            {
                Part = staff, Voice = null, Printed = true,
                Track = LyricBindings.TracksOfPart(root, staff) is [var words] ? words : l.Track,
            }
            : l).ToList();
    }

    private static IReadOnlyList<Line> BuildLines(SyntaxNode root)
    {
        var declarations = root.SitesOfKinds(SyntaxKind.SingerDeclaration).OfType<SingerDeclarationSyntax>().ToList();
        if (declarations.Count == 0)
            return [];
        var parts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in root.SitesOfKinds(Editing.PartReferenceFinder.DeclaringKinds))
            if (Editing.PartReferenceFinder.DeclaredName(n) is { } declared)
                parts.Add(declared.Text);
        var specs = RenderSpecParser.FindAll(root);
        var lines = new List<Line>(declarations.Count);
        foreach (var declaration in declarations)
        {
            string track = declaration.TrackName;
            int score = -1;
            for (SyntaxNode? n = declaration.Parent; n != null; n = n.Parent)
                if (n is RenderDeclarationSyntax render)
                    score = render.Span.Start;
            string? target = declaration.OnToken?.Text
                ?? (parts.Count == 1 ? parts.First()
                    : parts.Count == 0 ? SyntaxFacts.UnnamedPartName
                    : PrintedOn(score < 0 ? specs : specs.Where(s => s.SourceStart == score).ToList(), track));
            string? part = target, voice = null;
            if (target != null && parts.Count > 0 && !parts.Contains(target))
            {
                voice = target;
                part = parts.FirstOrDefault(p => LyricBindings.VoicesOfPart(root, p).Contains(target));
            }
            // No name written: the track the part names (§10); of several, see below.
            if (declaration.TrackToken == null && part != null && LyricBindings.TracksOfPart(root, part) is [var own])
                track = own;
            lines.Add(new Line(declaration.Singer, track, part, voice, declaration, score, declaration.PersonToken?.Text));
        }
        // …or, of several the part names, the ONE in the singer's language — the line's own
        // `language`, else the file's (owner's decision 2026-11-12: "alice sings lead, and lead
        // names its English words: the line need not say en"). None or several in it: an error,
        // said by the validator (LYS4054), and the unnamed track meanwhile.
        if (lines.Any(l => l.Node.TrackToken == null && l.Part != null && l.Track == SyntaxFacts.UnnamedLyricsName))
        {
            var languageOf = LyricLanguage.ReaderOf(root);
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.Node.TrackToken != null || line.Part == null || LyricBindings.TracksOfPart(root, line.Part) is not { Count: > 1 } several)
                    continue;
                string language = LyricLanguage.CodeOf(line.Node.LanguageToken?.Text) ?? languageOf(null);
                if (several.Where(t => languageOf(t) == language).ToList() is [var fits])
                    lines[i] = line with { Track = fits };
            }
        }
        // Lines of one name are one singer: the voice is said on one of them and is the others' too.
        for (int i = 0; i < lines.Count; i++)
            if (lines[i] is { Person: { } person, Singer: null }
                && lines.FirstOrDefault(l => l.Person == person && l.Singer != null) is { } named)
                lines[i] = lines[i] with { Singer = named.Singer };
        return lines;
    }

    /// <summary>The one part every score prints <paramref name="track"/> under, or null.</summary>
    private static string? PrintedOn(IReadOnlyList<RenderSpec> specs, string track)
    {
        var on = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spec in specs)
        {
            foreach (var binding in spec.GetVoiceBindings())
                if (!binding.WithLyrics.IsDefaultOrEmpty && binding.WithLyrics.Contains(track))
                    on.Add(binding.VoiceName);
            foreach (var row in spec.AllLyricsRows)
                if (row.PartName == track && row.Sings is { } sings)
                    on.Add(sings);
        }
        return on.Count == 1 ? on.First() : null;
    }

    /// <summary>
    /// A reader of whether a track's words on a part are SUNG in the score <paramref name="spec"/>:
    /// a <c>singer</c> line names them on that part and the part's staff sounds a voice.
    /// </summary>
    public static Func<string?, string?, bool> SungReaderOf(SyntaxNode root, RenderSpec? spec)
    {
        var lines = LinesFor(root, spec);
        if (lines.Count == 0 || spec == null)
            return static (_, _) => false;
        var sounds = SungStaves.Of(root, spec);
        return (track, part) => part != null && sounds.Sung.Contains(part, StringComparer.Ordinal)
            && lines.Any(l => l.Part == part && l.Track == track);
    }

    /// <summary><paramref name="spec"/> with every <c>singer</c> line's words on its part, where it does
    /// not print them there, so what is sung is read the way what is printed is. The words are the
    /// line's to sing whether the page shows them or not (docs/lyrics-singer-design.md §2).</summary>
    /// <remarks>This spec is read and never drawn. A sung track's words all go on staves: the part's
    /// own, a staff added for a part with none of its own (not drawn, or drawn on a tab or a staff of
    /// several parts). A row is known by its track's name alone, so a row of a sung track would take
    /// the track off the staves that print it, and two rows of one track would be one (a choir's
    /// four voices on one track sang as one, owner's report 2026-11-10). A sung track's rows move
    /// too, each to the part it follows, so its words there are kept whether a line sings them or not.</remarks>
    public static RenderSpec WithSungWords(SyntaxNode root, RenderSpec spec)
    {
        var lines = LinesFor(root, spec);
        if (lines.Count == 0)
            return spec;
        var sungTracks = lines.Select(l => l.Track).ToHashSet(StringComparer.Ordinal);
        var bound = SungSyllables.WithUnboundRowsBound(root, spec);
        var rows = new List<LyricsRowSpec>();
        var items = WithoutRows(bound.Items, sungTracks, rows);
        var placed = new HashSet<(string Track, string On)>();
        foreach (var binding in spec.GetVoiceBindings())
            foreach (var track in binding.WithLyrics)
                placed.Add((track, binding.VoiceName));
        void Place(string track, string part, string sings)
        {
            if (placed.Contains((track, part)) || !placed.Add((track, sings)))
                return;
            items = WithVerse(root, items, part, track, sings)
                ?? items.Add(new SingleStaffSpec(RenderSpecParser.WithFoldedVerse(root, new StaffSpec(ClefType.Treble, part), track, sings)));
        }
        foreach (var row in rows)
            Place(row.PartName, PartOf(root, row.Sings!), row.Sings!);
        foreach (var line in lines)
            if (line.Part is { } part)
                Place(line.Track, part, line.Voice ?? part);
        return items == spec.Items ? spec : spec with { Items = items };
    }

    /// <summary><paramref name="items"/> without the rows of <paramref name="tracks"/> that follow
    /// a part, a group's included; those rows are added to <paramref name="taken"/> in written order.</summary>
    private static ImmutableArray<RenderItemSpec> WithoutRows(ImmutableArray<RenderItemSpec> items, HashSet<string> tracks, List<LyricsRowSpec> taken)
    {
        var kept = ImmutableArray.CreateBuilder<RenderItemSpec>(items.Length);
        foreach (var item in items)
            switch (item)
            {
                case LyricsRowSpec { Sings: not null } row when tracks.Contains(row.PartName):
                    taken.Add(row);
                    break;
                case GrandStaffRenderSpec group:
                    var members = WithoutRows(group.GrandStaff.Members, tracks, taken);
                    kept.Add(members.SequenceEqual(group.GrandStaff.Members) ? group : new GrandStaffRenderSpec(group.GrandStaff with { Members = members }));
                    break;
                default:
                    kept.Add(item);
                    break;
            }
        return kept.Count == items.Length && kept.SequenceEqual(items) ? items : kept.ToImmutable();
    }

    /// <summary>The part a row's target is: the part of that name, else the part with a voice of that name.</summary>
    private static string PartOf(SyntaxNode root, string target)
    {
        foreach (var n in root.SitesOfKinds(Editing.PartReferenceFinder.DeclaringKinds))
            if (Editing.PartReferenceFinder.DeclaredName(n) is { } declared && LyricBindings.VoicesOfPart(root, declared.Text).Contains(target))
                return declared.Text;
        return target;
    }

    /// <summary><paramref name="items"/> with the verse on the first staff of <paramref name="part"/>, or null where it has none.</summary>
    private static ImmutableArray<RenderItemSpec>? WithVerse(SyntaxNode root, ImmutableArray<RenderItemSpec> items, string part, string track, string sings)
    {
        for (int i = 0; i < items.Length; i++)
        {
            switch (items[i])
            {
                case SingleStaffSpec staff when staff.Staff.VoiceName == part:
                    return items.SetItem(i, new SingleStaffSpec(RenderSpecParser.WithFoldedVerse(root, staff.Staff, track, sings)));
                case GrandStaffRenderSpec group when WithVerse(root, group.GrandStaff.Members, part, track, sings) is { } members:
                    return items.SetItem(i, new GrandStaffRenderSpec(group.GrandStaff with { Members = members }));
            }
        }
        return null;
    }

    /// <summary>A reader of who sings a track on a part: the <c>singer</c> line's string, the lines'
    /// joined by <see cref="Joiner"/> where several sing it, or null (the part's default voice).</summary>
    public static Func<string?, string?, string?> ReaderOf(SyntaxNode root, RenderSpec? spec)
    {
        var lines = LinesFor(root, spec);
        return (track, part) =>
        {
            var singers = Voices(lines, track, part).Select(v => v.Singer).ToList();
            return singers.Count <= 1 ? singers.FirstOrDefault() : string.Join(Joiner, singers.Select(s => s ?? ""));
        };
    }

    /// <summary>
    /// A reader of the singer's OWN name (<c>singer carol …</c>) for one voice of a part — its
    /// track, its part and its place among the voices on those words (<see cref="Midi.SungVoice.Line"/>:
    /// one for each line that differs in who sings or in how, in the order written). Null for a
    /// voice whose line gives no name.
    /// </summary>
    public static Func<string?, string?, int, string?> NamesReaderOf(SyntaxNode root, RenderSpec? spec)
    {
        var lines = LinesFor(root, spec);
        return (track, part, at) =>
        {
            // By the track too where the voice says one the lines know; else by the part alone.
            var sung = lines.Where(l => l.Part == part && l.Track == track).ToList();
            if (sung.Count == 0)
                sung = lines.Where(l => l.Part == part).ToList();
            var voices = sung
                .GroupBy(l => (l.Singer, Settings: SettingsText(l.Node, place: !l.Printed)))
                .Select(g => g.Select(l => l.Person).FirstOrDefault(p => p != null)).ToList();
            return at < voices.Count ? voices[at] : null;
        };
    }

    /// <summary>What separates the settings of several voices singing one track on one part
    /// (<see cref="SettingsReaderOf"/>): a character no setting holds.</summary>
    public const char SettingsJoiner = '\u001F';

    /// <summary>A reader of HOW each of those voices sings (its line's <c>with …</c>), beside
    /// <see cref="ReaderOf"/>: the settings of each voice as <see cref="SettingsText"/>, in the
    /// same order and number, joined by <see cref="SettingsJoiner"/>; null where none sets any.</summary>
    public static Func<string?, string?, string?> SettingsReaderOf(SyntaxNode root, RenderSpec? spec)
    {
        var lines = LinesFor(root, spec);
        return (track, part) =>
        {
            var settings = Voices(lines, track, part).Select(v => v.Settings).ToList();
            return settings.All(s => s.Length == 0) ? null : string.Join(SettingsJoiner, settings);
        };
    }

    /// <summary>The voices that sing a track on a part: one for each line that differs in who
    /// sings or in how, in the order written.</summary>
    private static List<(string? Singer, string Settings)> Voices(IReadOnlyList<Line> lines, string? track, string? part)
        => lines.Where(l => l.Part == part && l.Track == track).Select(l => (l.Singer, Settings: SettingsText(l.Node, place: !l.Printed))).Distinct().ToList();

    /// <summary>
    /// A singer line's settings as one text: <c>name=value</c> a line, a number as written in
    /// the invariant culture, a word or a string between quotes; a name written twice keeps its
    /// last value. Empty with no <c>with</c>. Read back by <see cref="SettingsOf"/>.
    /// </summary>
    public static string SettingsText(SingerDeclarationSyntax line, bool place = true)
    {
        var last = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var setting in line.Settings)
        {
            // Not an engine's to hear: said as an error where it is written (LyricSingsValidator).
            if (setting.Name == RefusedSetting)
                continue;
            last[setting.Name] = setting.Number is { } number
                ? number.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                : "\"" + setting.Word + "\"";
        }
        // …and what of the part the line sings (its up / down / octave / in), under names no
        // engine has: Midi.SungVoices takes them out again before the settings go to a voice.
        // They ride here so that two lines that differ only in these are two voices (Voices).
        if (!place)
            return Joined(last);
        if (line.Interval != 0)
            last[PlaceInterval] = line.Interval.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (line.Octaves != 0)
            last[PlaceOctaves] = line.Octaves.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (line.Sections.Count > 0)
            last[PlaceSections] = "\"" + string.Join(",", line.Sections.Select(s => s.Text)) + "\"";
        return Joined(last);
    }

    private static string Joined(Dictionary<string, string> settings)
        => string.Join('\n', settings.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value));

    /// <summary>
    /// The one setting name Lily# refuses after <c>with</c>, whatever engine sings (owner's
    /// decision 2026-11-12): moving a voice's notes is the singer line's own work — <c>up 3</c>
    /// along the key, <c>octave -1</c> — and an engine's semitone transpose (NEUTRINO's -f) is a
    /// harmony out of the key. It is an error where written and never reaches a voice.
    /// </summary>
    public const string RefusedSetting = "transpose";

    /// <summary>The words of a singer line that say what of its part the voice sings
    /// (docs/lyrics-singer-design.md §12), for the editor.</summary>
    public const string UpWord = SyntaxFacts.SingerUpWord;

    /// <inheritdoc cref="UpWord"/>
    public const string DownWord = SyntaxFacts.SingerDownWord;

    /// <inheritdoc cref="UpWord"/>
    public const string OctaveWord = SyntaxFacts.SingerOctaveWord;

    /// <inheritdoc cref="UpWord"/>
    public const string InWord = SyntaxFacts.SingerInWord;

    /// <inheritdoc cref="UpWord"/>
    public const string WithWord = SyntaxFacts.SingerWithWord;

    /// <summary>The names <see cref="SettingsText"/> carries a line's place under: the interval of
    /// <c>up N</c> / <c>down N</c> (signed), the octaves of <c>octave N</c>, the sections of
    /// <c>in A B</c> joined by commas. A control character begins each: no setting is so named.</summary>
    public const string PlaceInterval = "\u0001interval";

    /// <inheritdoc cref="PlaceInterval"/>
    public const string PlaceOctaves = "\u0001octaves";

    /// <inheritdoc cref="PlaceInterval"/>
    public const string PlaceSections = "\u0001in";

    /// <summary>The settings of one voice's <see cref="SettingsText"/>: a name its number, or its word.</summary>
    public static IReadOnlyDictionary<string, (double? Number, string? Word)> SettingsOf(string? text)
    {
        var settings = new Dictionary<string, (double? Number, string? Word)>(StringComparer.Ordinal);
        foreach (string line in (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int at = line.IndexOf('=');
            if (at <= 0)
                continue;
            string value = line[(at + 1)..];
            settings[line[..at]] = value.StartsWith('"')
                ? (null, value.Trim('"'))
                : (double.Parse(value, System.Globalization.CultureInfo.InvariantCulture), null);
        }
        return settings;
    }
}
