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

using LilySharp.Core.Editing;
using LilySharp.Core.Music;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The lyric-binding rules (docs/lyrics-singer-design.md): a score row places a track's words,
/// under the staff nearest above it or, with its own <c>on</c>, under any other part's. See
/// <see cref="LyricBindings"/>.
/// <list type="bullet">
/// <item>LYS7004 — <c>on T</c> where T names no part or voice in the file.</item>
/// <item>LYS6012 — a row inside a staff group that does not follow the staff
/// directly above it (a group has no independent band to fall back to).</item>
/// </list>
/// (LYS6009/LYS6010 guarded the <c>with lyrics</c> clause against unbound and
/// wrong-staff tracks; both RETIRED with the clause — LYS0031. At top level a
/// bound row folds only onto the staff it sings, a row for another part is a
/// legal independent band, and an unbound row is the lead-sheet row.)
/// </summary>
internal sealed class LyricSingsValidator : ISemanticValidator
{
    private static readonly HashSet<string> ReservedTrackNames = new(StringComparer.Ordinal) { "on", "hum", "solfege" };

    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();

        // Every name that can be sung: declared parts, section part blocks
        // (implicit parts), and named voices anywhere in music.
        var partNames = new HashSet<string>(StringComparer.Ordinal);
        var voiceNames = new HashSet<string>(StringComparer.Ordinal);
        // ⚠️ Both sets are read through PartReferenceFinder, which the language server's
        // semantic tokens also ask: a `sings` target the editor paints as resolved while
        // this validator calls it unknown would be the editor contradicting itself inside
        // one line. Two callers, one answer — and the wider set (parts AND voices) is the
        // half a caller is most likely to get wrong on its own.
        // Each predicate is asked of the kinds it can answer on — its list beside it in
        // PartReferenceFinder, pinned to the predicate by SymbolKindsTests (the declaring
        // kinds) and LyricSingsWalkTests (the other two) — rather than of every node: the
        // sets below name a handful of nodes in a book of hundreds of thousands, and this
        // pass runs after every settled keystroke.
        foreach (var n in root.DescendantNodesOfKinds(PartReferenceFinder.DeclaringKinds))
            if (PartReferenceFinder.DeclaredName(n) is { } part)
                partNames.Add(part.Text);
        foreach (var n in root.DescendantNodesOfKinds(PartReferenceFinder.VoiceIntroducingKinds))
            PartReferenceFinder.CollectVoiceNames(n, voiceNames);

        // Both sites that name a melody — a score row's `on` and a singer line's — go through the
        // unknown-target net. The singer line reports its own (CheckSingerLines), so only the row
        // is read here; PartReferenceFinder.SingsTargetToken answers for both.
        static (string? Name, string? Target, TextSpan Span)? SingsOf(SyntaxNode node) => node switch
        {
            LyricsRowRenderSyntax r => (r.PartName, r.SingsTarget, (r.SingsKeyword ?? r.LyricsKeyword).Span),
            _ => null,
        };

        foreach (var node in root.DescendantNodesOfKinds(PartReferenceFinder.SingsKinds))
        {
            if (SingsOf(node) is not ({ } name, { } target, var span))
                continue;
            if (!partNames.Contains(target) && !voiceNames.Contains(target))
                _diagnostics.Error(
                    span,
                    DiagnosticCodes.SingsTargetUnknown,
                    $"'{name}' is set on '{target}', but no part or voice of "
                    + "that name exists in this file.");
        }

        // A track's name only names it (owner's decision 2026-11-10): the
        // same name reads as a binding, and is none when the part is renamed. Said once a track.
        var namedTracks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in root.DescendantNodes<LyricsBlockSyntax>())
        {
            if (block.NameToken is not { } token || !namedTracks.Add(token.Text))
                continue;
            string? kind = partNames.Contains(token.Text) ? "part" : voiceNames.Contains(token.Text) ? "voice" : null;
            if (kind != null)
                _diagnostics.Error(token.Span, DiagnosticCodes.LyricsNamedAsSinger,
                    $"lyrics '{token.Text}' has the name of a {kind}. Give the words a name of their own and say who sings them on its own line: "
                    + $"'lyrics words {{ … }}' and 'singer sings words on {token.Text}'.");
        }

        // (The `with lyrics` attachment checks — LYS6009/LYS6010 — died with the
        // clause, LYS0031: a bound row folds only onto the staff it sings, so a
        // wrong-staff or unbound attachment can no longer be SPELLED at top level;
        // what remains checkable is the group case below.)

        CheckLyricsBlocks(root);
        CheckSingerLines(root, partNames, voiceNames);
        CheckStaffSounds(root);
        WarnRowsAPartDoesNotList(root, partNames);

        foreach (var row in root.DescendantNodes<LyricsRowRenderSyntax>())
        {
            if (row.PartName == GeneratedLyrics.Hum)
                _diagnostics.Error(row.LyricsKeyword.Span, DiagnosticCodes.LyricsHumNotPrinted,
                    "Humming has no words to print: 'singer sings hum on PART' sings it.");
            else if (row.PartName == GeneratedLyrics.Solfege && RenderSpecParser.RowTarget(row) == null)
                _diagnostics.Error(row.LyricsKeyword.Span, DiagnosticCodes.LyricsRowNeedsOn,
                    "The note names follow notes: say whose, 'lyrics solfege on PART'.");
            else if (row.SingsTarget == null && LyricBindings.TargetOf(root, row.PartName) == null)
            {
                RenderSpecParser.NearestStaffAbove(row, out bool severalParts);
                if (severalParts)
                    _diagnostics.Error(row.LyricsKeyword.Span, DiagnosticCodes.LyricsRowNeedsOn,
                        $"The staff above lyrics '{row.PartName}' carries several parts: say whose notes the words follow, 'lyrics {row.PartName} on PART'.");
            }
        }

        // Rows inside a staff group: inside the braces a row IS the staff above's
        // attached verse (score = a vertical stack of bands), so a row that sings
        // no adjacent staff has no place a group can give it. The fold itself is
        // RenderSpecParser.ParseGrandStaff; this is its refusal half.
        foreach (var group in root.DescendantNodes<GrandStaffRenderSyntax>())
        {
            string? partAbove = null;
            bool sharedStaffAbove = false;
            bool nestedGroupAbove = false;
            foreach (var member in group.ChildNodes())
            {
                switch (member)
                {
                    case StaffRenderSyntax st:
                        partAbove = RenderSpecParser.ParseStaffSpec(st)?.VoiceName;
                        sharedStaffAbove = false;
                        nestedGroupAbove = false;
                        break;
                    // A nested group: its own rows are checked when this loop reaches it as a
                    // group, and a row standing AFTER it, outside its braces, has no staff of
                    // this group directly above it (ParseGrandStaff folds under plain staves only).
                    case GrandStaffRenderSyntax:
                        partAbove = null;
                        sharedStaffAbove = false;
                        nestedGroupAbove = true;
                        break;
                    // A condensed or combined staff carries several parts, so no row can be
                    // ITS verse — RenderSpecParser.ParseGrandStaff folds only under a plain
                    // staff, and this is the refusal half of that.
                    case CondensedStaffRenderSyntax or CombinedStaffRenderSyntax:
                        partAbove = null;
                        sharedStaffAbove = true;
                        nestedGroupAbove = false;
                        break;
                    // A row with its own `on` stands anywhere in the group, on its own band
                    // (RenderSpecParser.ParseGrandStaff). Only a row that says nothing can
                    // lack a staff to belong to.
                    case LyricsRowRenderSyntax row
                        when row.SingsTarget == null
                          && (partAbove == null
                              || !RenderSpecParser.RowBindsToPart(root, row.PartName, RenderSpecParser.RowTarget(row), partAbove)):
                        _diagnostics.Error(
                            row.LyricsKeyword.Span,
                            DiagnosticCodes.GroupRowNotBoundToStaffAbove,
                            nestedGroupAbove
                                ? $"lyrics '{row.PartName}' stands under a nested group - inside a group "
                                  + "a row is the verse of the plain staff directly above it; write the row "
                                  + "inside that group, under the staff it sings."
                            : sharedStaffAbove
                                ? $"lyrics '{row.PartName}' stands under a condensed or combined staff - "
                                  + "inside a group a row is the verse of the ONE part on the staff above it, "
                                  + "and that staff carries several; move the row outside the braces."
                            : partAbove == null
                                ? $"lyrics '{row.PartName}' stands before any staff in this group - "
                                  + "inside a group a row is the verse of the staff directly above it."
                                : $"lyrics '{row.PartName}' does not sing '{partAbove}', the staff "
                                  + "directly above it - inside a group a row is that staff's verse; "
                                  + $"write 'lyrics {row.PartName} on PART' to say whose notes the words follow.");
                        break;
                }
            }
        }

        foreach (var group in root.DescendantNodes<GrandStaffRenderSyntax>())
            if (!group.IsInside<GrandStaffRenderSyntax>())
                WarnRowsPlacedTwice(group);
    }

    /// <summary>The same words on the same part, written twice in one group: two rows saying
    /// one thing (docs/lyrics-singer-design.md §9.3). Said at the second and any later row.</summary>
    private void WarnRowsPlacedTwice(GrandStaffRenderSyntax group)
    {
        var placed = new HashSet<(string Track, string Part)>();
        foreach (var row in group.DescendantNodes<LyricsRowRenderSyntax>())
            if (RenderSpecParser.RowTarget(row) is { } part && !placed.Add((row.PartName, part)))
                _diagnostics.Warning(row.LyricsKeyword.Span, DiagnosticCodes.GroupRowPlacedTwice,
                    $"lyrics '{row.PartName}' is already placed on '{part}' in this group; the same words print twice.");
    }

    private void CheckLyricsBlocks(SyntaxNode root)
    {
        foreach (var block in root.DescendantNodes<LyricsBlockSyntax>())
        {
            string name = block.NameToken?.Text ?? "words";
            if (block.NameToken is { } token && ReservedTrackNames.Contains(token.Text))
                _diagnostics.Error(token.Span, DiagnosticCodes.LyricsNameReserved,
                    $"'{token.Text}' is a reserved word and cannot name lyrics.");
            if (block.IsInside<PartDeclarationSyntax>() || block.IsInside<PartBlockSyntax>())
                _diagnostics.Error(block.LyricsKeyword.Span, DiagnosticCodes.LyricsInsidePart,
                    "Lyrics cannot be written inside a part: words are not a part's. Write them at the top level or directly in a section, 'lyrics words { section A { … } }', and name them in the part's header with 'lyrics words'.");
        }
    }

    /// <summary>A score row giving a part words it does not list, where it lists any
    /// (docs/lyrics-singer-design.md §10).</summary>
    private void WarnRowsAPartDoesNotList(SyntaxNode root, HashSet<string> partNames)
    {
        if (!LyricBindings.AnyPartNamesWords(root))
            return;
        foreach (var row in root.DescendantNodes<LyricsRowRenderSyntax>())
        {
            if (RenderSpecParser.RowTarget(row) is not { } target)
                continue;
            string? part = partNames.Contains(target) ? target : partNames.FirstOrDefault(p => LyricBindings.VoicesOfPart(root, p).Contains(target));
            if (part != null)
                WarnIfNotListed(root, part, row.NameToken?.Text, row.PartName, (row.NameToken ?? row.LyricsKeyword).Span);
        }
    }

    private void WarnIfNotListed(SyntaxNode root, string part, string? written, string track, TextSpan at)
    {
        var listed = LyricBindings.TracksOfPart(root, part);
        if (listed.Count == 0 || listed.Contains(track))
            return;
        // The staff of what a singer sings is a part Lily# makes (Editing.SingerParts): its
        // header is not the writer's to add a track to, and its name is not the writer's to
        // read (owner's report 2026-11-12: `lyrics solfege` under `staff alice` warned that
        // "part alice__2" did not list it). Any row may stand under it.
        if (LyricSinger.IsMadePart(root, part))
            return;
        _diagnostics.Warning(at, DiagnosticCodes.TrackNotListedByPart, written == null
            ? $"Part '{part}' names {string.Join(", ", listed.Select(t => $"'{t}'"))}, not the unnamed lyrics."
            : $"Part '{part}' does not list lyrics '{written}'. If these are its words too, add 'lyrics {written}' to the part.");
    }

    private void CheckSingerLines(SyntaxNode root, HashSet<string> partNames, HashSet<string> voiceNames)
    {
        var lines = LyricSinger.LinesOf(root);
        if (lines.Count == 0)
            return;
        var tracks = root.DescendantNodes<LyricsBlockSyntax>().Select(b => b.VoiceName).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var wordsOfPart = new Dictionary<(int Score, string Part), string>();
        var voicesOnPart = new HashSet<(int Score, string Part, string? Singer)>();
        foreach (var line in lines)
        {
            var node = line.Node;
            if (node.SingsKeyword == null)
                continue;   // the parser said so
            // A singer called as a PART is: a score's `staff NAME` is then that part, never what
            // she sings, so her harmony could not be printed and nothing would say why. Allowed
            // in the one case where the part IS her notes — written out, the line singing it as
            // it stands (owner's question 2026-11-12: "was a part's name not refused?").
            if (node.PersonToken is { } person && partNames.Contains(person.Text)
                && !(line.Part == person.Text && line.Voice == null && node.Interval is >= -1 and <= 1 && node.Octaves == 0))
                _diagnostics.Error(person.Span, DiagnosticCodes.SingerNamedAsPart,
                    $"'{person.Text}' is a part's name, so it cannot be this singer's: in a score, 'staff {person.Text}' is that part. "
                    + "Give the singer another name. (A singer may be called as a part is only where the part is her own notes, written out: 'sings on "
                    + person.Text + "' with no 'up', 'down' or 'octave'.)");
            var at = node.TrackToken ?? node.SingsKeyword;
            var own = line.Part is { } sungPart ? LyricBindings.TracksOfPart(root, sungPart) : [];
            // Of several the part names, the one in the singer's language is sung (LyricSinger.LinesOf);
            // with none or several in it, the line has to say which.
            if (node.TrackToken == null && own.Count > 1 && !own.Contains(line.Track))
            {
                var languageOf = LyricLanguage.ReaderOf(root);
                string language = LyricLanguage.CodeOf(node.LanguageToken?.Text) ?? languageOf(null);
                string name = LyricLanguage.Names.FirstOrDefault(n => LyricLanguage.CodeOf(n) == language) ?? language;
                int fitting = own.Count(t => languageOf(t) == language);
                _diagnostics.Error(at.Span, DiagnosticCodes.SingerWordsAmbiguous,
                    $"'{line.Part}' names several lyrics ({string.Join(", ", own)}), and "
                    + (fitting == 0 ? $"none of them is in {name}" : $"{fitting} of them are in {name}")
                    + (node.LanguageToken != null ? ", the language this line says" : ", the file's language")
                    + $": say which, 'singer sings {own[0]} on {line.Part}'"
                    + (node.LanguageToken == null ? ", or say the singer's language as its gender is said, 'singer japanese sings on " + line.Part + "'." : "."));
                continue;
            }
            // A name the part lists and no block defines is already said at the part (LYS1007).
            if (!GeneratedLyrics.IsGenerated(line.Track) && !tracks.Contains(line.Track) && (node.TrackToken != null || own.Count == 0))
                _diagnostics.Error(at.Span, DiagnosticCodes.SingerWordsUnknown,
                    node.TrackToken == null ? "This file has no unnamed lyrics to sing." : $"No lyrics are named '{line.Track}'.");
            if (node.OnToken is { } on)
            {
                if (!partNames.Contains(on.Text) && !voiceNames.Contains(on.Text))
                    _diagnostics.Error(on.Span, DiagnosticCodes.SingsTargetUnknown, $"No part or voice is named '{on.Text}'.");
                else if (!partNames.Contains(on.Text) && partNames.Count(p => LyricBindings.VoicesOfPart(root, p).Contains(on.Text)) > 1)
                    _diagnostics.Error(on.Span, DiagnosticCodes.SingerVoiceAmbiguous, $"Several parts have a voice named '{on.Text}'.");
            }
            else if (line.Part == null)
                _diagnostics.Error(at.Span, DiagnosticCodes.SingerPartUnresolved,
                    "Nothing says whose notes these words are sung on: add 'on PART'.");
            if (line.Part is not { } part)
                continue;
            if (node.TrackToken is { } named)
                WarnIfNotListed(root, part, named.Text, line.Track, named.Span);
            // One scope, one set of words a part; a second voice on the same words sings along.
            if (!wordsOfPart.TryAdd((line.Score, part), line.Track) && wordsOfPart[(line.Score, part)] != line.Track)
                _diagnostics.Error(node.Keyword.Span, DiagnosticCodes.SingerDuplicatePart, line.Score < 0
                    ? $"'{part}' already sings '{wordsOfPart[(line.Score, part)]}'. To have another score sing '{line.Track}' on it, write this line inside that score."
                    : $"'{part}' already sings '{wordsOfPart[(line.Score, part)]}' in this score, and one part sings one set of words.");
            // …unless it sings another line of it (up 3), or in other sections.
            else if (!voicesOnPart.Add((line.Score, part, line.Singer + "\u0001" + node.Interval + "\u0001" + node.Octaves
                         + "\u0001" + string.Join(",", node.Sections.Select(s => s.Text)))))
                _diagnostics.Warning(node.Keyword.Span, DiagnosticCodes.SingerRepeated,
                    $"This voice already sings '{line.Track}' on '{part}'.");
            CheckPlace(root, node);
        }
    }

    /// <summary>
    /// A singer line's <c>up N</c> / <c>down N</c> / <c>octave N</c> / <c>in SECTION …</c>
    /// (docs/lyrics-singer-design.md §12): an interval from a second to two octaves, an octave
    /// or three either way, sections the file has, each named once.
    /// </summary>
    private void CheckPlace(SyntaxNode root, SingerDeclarationSyntax node)
    {
        if (node.IntervalToken is { } interval && Math.Abs(node.Interval) is < 2 or > 15)
            _diagnostics.Error(interval.Span, DiagnosticCodes.SingerPlaceMalformed,
                $"'{interval.Text}' is no interval to sing at: up 3 is a third above in the key, down 6 a sixth below (2 to 15).");
        if (node.OctaveToken is { } octave && Math.Abs(node.Octaves) is < 1 or > 3)
            _diagnostics.Error(octave.Span, DiagnosticCodes.SingerPlaceMalformed,
                $"'octave {octave.Text}': a voice sings one to three octaves above (octave 1) or below (octave -1) its part.");
        foreach (var setting in node.Settings)
            if (setting.Name == LyricSinger.RefusedSetting)
                _diagnostics.Error(setting.NameToken.Span, DiagnosticCodes.SingerPlaceMalformed,
                    "'transpose' is not a setting of a singer line: it would move the voice by semitones, out of the key. "
                    + "Write 'up 3' (a third above, in the key), 'down 6' or 'octave -1' after the part, before 'with'.");
        if (node.Sections.Count == 0)
            return;
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in root.SitesOfKinds(SyntaxKind.SectionDeclaration).OfType<SectionDeclarationSyntax>())
            declared.Add(section.SectionName);
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in node.Sections)
        {
            if (!declared.Contains(section.Text))
                _diagnostics.Error(section.Span, DiagnosticCodes.SingerPlaceMalformed,
                    $"No section is named '{section.Text}'"
                    + (declared.Count is > 0 and <= 12 ? $" (there is: {string.Join(", ", declared)})." : "."));
            else if (!named.Add(section.Text))
                _diagnostics.Warning(section.Span, DiagnosticCodes.SingerPlaceMalformed, $"'{section.Text}' is named twice.");
        }
    }

    private void CheckStaffSounds(SyntaxNode root)
    {
        var lines = LyricSinger.LinesOf(root);
        void Check(SyntaxTokenNode? word, string? part)
        {
            if (word == null)
                return;
            // Sung where it is written: the top level's lines, and those of the score around the staff.
            int score = -1;
            for (SyntaxNode? n = word.Parent; n != null; n = n.Parent)
                if (n is RenderDeclarationSyntax render)
                    score = render.Span.Start;
            if (word.Text is not ("voice" or "instrument" or "both"))
                _diagnostics.Error(word.Span, DiagnosticCodes.StaffSoundUnknown, $"'sounds' takes voice, instrument or both, not '{word.Text}'.");
            else if (word.Text != "instrument" && (part == null || !lines.Any(l => l.Part == part && (l.Score < 0 || l.Score == score))))
                _diagnostics.Error(word.Span, DiagnosticCodes.StaffSoundNeedsSinger,
                    $"No singer sings '{part}'. Add a line: 'singer S sings WORDS on {part}'.");
        }
        foreach (var render in root.DescendantNodes<RenderDeclarationSyntax>())
            if (RenderSpecParser.Parse(render) is { Items.IsEmpty: true, HiddenStaves.IsDefaultOrEmpty: false })
                _diagnostics.Error(render.RenderKeyword.Span, DiagnosticCodes.ScoreAllStavesHidden,
                    "Every staff of this score is hidden, so it has nothing to print. Show at least one staff.");
        foreach (var node in root.DescendantNodes())
            if (node is StaffRenderSyntax staff)
                Check(staff.SoundsToken, staff.PartToken?.Text ?? SyntaxFacts.UnnamedPartName);
            else if (node is TabRenderSyntax tab)
                Check(tab.SoundsToken, tab.PartToken?.Text ?? SyntaxFacts.UnnamedPartName);
    }
}
