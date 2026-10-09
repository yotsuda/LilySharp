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

using System.Text;
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

// A using directive does not import NESTED namespaces, so the green layer needs its own name
// here. Only the form walk touches it, to build the two nodes the source never wrote:
// the `|:` / `:|` a form's repeat block spells with bare tokens, and the ending node
// EmitInlineRepeat groups (AppendRepeatBlock / CreateEnding).
using InternalSyntax = LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.LilyPond;

// The form walk: the ordered music of a part (OrderedMusic), section plays and their boundaries
// (EmitSectionPlay), repeat blocks and volta endings, padding bars, and the container readings
// (MusicItems / ContainerMusic). Split out of LilyPondExporter.cs as a partial class
// (2026-10-02); same instance state, no behavior change.
public sealed partial class LilyPondExporter
{
    // Concatenate the part's sections into one item stream, in the order the
    // primary form references them. A |: … :| repeat can span several sections,
    // so grouping must happen AFTER this flattening (in EmitMusicStream).
    private List<SyntaxNode> OrderedMusic(
        string partName, PartDeclarationSyntax? part, FormDeclarationSyntax? form,
        List<SectionDeclarationSyntax> allSections)
    {
        // A section reaches this part in one of TWO spellings, and both must be read.
        //   by-part:    part m { section A { c8 d } }   — music inline in the section
        //   by-section: section A { m { c8 d } }        — the section sits OUTSIDE the
        //                                                    part and names it with a PartBlock
        // ⚠️ Only the first was read here. `allSections` was collected by the caller FOR the
        // second and then never used, so every file written the ordinary way exported an
        // EMPTY part variable — a valid .ly that renders a blank staff, silently. All ten
        // showcase fixtures and most of test/ are by-section.
        // (MusicXmlExporter.EmitGroupedByPartSection carries the mirror-image note: that exporter
        // was missing the OTHER spelling and had the same symptom.)
        // A part a score names but never declares (`ossia melody` with no `part melody`)
        // has no block of its own to hold sections — only the second and third spellings
        // can reach it.
        // The name-keyed header registry, rebuilt per part from the SAME document-order
        // walk the collector registers from (every declaration of a name contributes;
        // the played declaration may be a different node — see the field's remarks).
        _sectionHeaders = Semantics.SectionHeaders.Read(allSections);
        _allSections = allSections;

        var partSections = part?.DescendantNodes<SectionDeclarationSyntax>().ToList()
            ?? new List<SectionDeclarationSyntax>();
        // Keyed by the SECTION as well as its container, because the section's own header
        // (`section A { partial 4  m { … } }`) belongs to every part of it and the container
        // is usually the part cell one level down — see SectionHeaderMusic.
        var byName = new Dictionary<string, (SectionDeclarationSyntax Section, SyntaxNode Container)>(
            StringComparer.Ordinal);
        var inOrder = new List<(SectionDeclarationSyntax Section, SyntaxNode Container)>();
        foreach (var s in allSections)
        {
            // `allSections` is a DESCENDANT walk of the whole file, so it already carries
            // both spellings in document order; a section belonging to some OTHER part
            // holds no PartBlock of ours and drops out.
            SyntaxNode? container = partSections.Contains(s)
                ? s
                : PartBlockBody(s.DescendantNodes<PartBlockSyntax>()
                    .FirstOrDefault(b => b.Name == partName));
            if (container == null)
            {
                // THE THIRD spelling, and it was missing for the same reason the second was.
                // `part bl { clef bass }  section A { c d e }` — the lone part's music written
                // straight into a top-level section, with no cell wrapping it. The collector
                // reads it (MeasureCollector.Form.cs "Single-part shorthand"), so a book
                // written this way renders; the exporter dropped it SILENTLY — no warning, an
                // empty part variable, a valid .ly with a blank staff. 35 of the 204 fixtures
                // are written this way, including every book that reaches a tab staff.
                // ⚠ The guard asks THE canonical question (MeasureCollector's
                // SectionHasInlineMusic — the same predicate the collector's own
                // "Single-part shorthand" arm asks), not "does LooseSectionMusic yield
                // anything", which is what it used to ask: that list counts a DIRECTIVE
                // as music. A directives-only top-level section is a section HEADER
                // (`section A { key g major }` beside `part m { section A { … } }`), and
                // this dictionary is last-declaration-wins — so a header written AFTER
                // the part overwrote the part's real declaration and the twin played the
                // HEADER: `\key g \major \key g \major`, the directive twice (once from
                // the name-keyed registry, once as this "music") and not one note, while
                // the page engraved the four notes. Written BEFORE the part the same book
                // was whole, so the two spellings differed by LINE ORDER alone.
                // The language already says a top-level section in a by-part file holds
                // only directives and the parts' cells (SectionMusicNeedsPartValidator
                // refuses music there), so the case this arm exists for — the lone part's
                // music with no cell around it — is exactly what the canonical predicate
                // admits, and a header is exactly what it turns away.
                if (s.Parent is CompilationUnitSyntax && SectionHasInlineMusic(s))
                {
                    byName[s.SectionName] = (s, s);
                    inOrder.Add((s, s));
                    _looseSections.Add(s);
                }
                continue;
            }
            byName[s.SectionName] = (s, container);
            inOrder.Add((s, container));
        }

        var result = new List<SyntaxNode>();
        if (form != null)
        {
            var formItems = FormWalk.Read(form);
            _repeatTiePlays = RepeatTiePlays(formItems, byName);
            _lpPlayIndex = 0;
            _firstPlayRestatesTempo = FirstPlayedSection(formItems) is { } first
                && _sectionHeaders.Tempos.ContainsKey(first);
            AppendFormItems(formItems, byName, result);
        }
        else
        {
            _firstPlayRestatesTempo = inOrder.Count > 0
                && _sectionHeaders.Tempos.ContainsKey(inOrder[0].Section.SectionName);
            foreach (var entry in inOrder)
            {
                // No form: sections play in declaration order and each is labelled with
                // its declared label, else its own name (MeasureCollector.LabelForDeclarationOrder),
                // with the same boundary key-restore a formed play gets.
                var headerMusic = SectionHeaderMusic(entry.Section).ToList();
                result.Add(new SectionPlayMarker(
                    Semantics.SectionLabelRule.LabelFor(referenceIsSilent: false,
                        displayLabel: SyntaxFacts.DeclaredSectionLabel(entry.Section, entry.Section.SectionName),
                        sectionName: entry.Section.SectionName),
                    headerMusic.Any(h => h is KeySignatureSyntax),
                    headerMusic.Any(h => h is TimeSignatureSyntax)));
                result.AddRange(headerMusic);
                if (_meterPlan.IsEmpty)
                    result.AddRange(ContainerMusic(entry.Container));
                else
                    result.AddRange(_meterPlan.WithForeignChanges(entry.Section.SectionName,
                        entry.Container, ContainerMusic(entry.Container).ToList()));
                result.AddRange(PaddingBars(entry.Container));
            }
        }

        // Also carry any part-level clef declared outside a section (e.g. a
        // mid-part clef change is inside a section and handled there).
        return result;
    }

    /// <summary>
    /// True when the first section the part plays carries a header <c>tempo</c>: it stands
    /// at the same moment as the file's, and LilyPond keeps one tempo event per moment —
    /// <see cref="EmitScoreSettings"/> then leaves the file's out (its remark says why).
    /// </summary>
    private bool _firstPlayRestatesTempo;

    /// <summary>The name of the first section a form plays, through a repeat's or an
    /// ending's first reference; null for a form that plays none.</summary>
    private static string? FirstPlayedSection(IReadOnlyList<FormWalk.Item> items)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case FormWalk.SectionRef s:
                    return s.Name;
                case FormWalk.Repeat r:
                    if (FirstPlayedSection(r.Children) is { } inRepeat)
                        return inRepeat;
                    break;
                case FormWalk.Ending e:
                    if (e.Sections.Count > 0)
                        return e.Sections[0].Name;
                    break;
            }
        }
        return null;
    }

    /// <summary>
    /// A section's OWN header directives, which belong to every part of it.
    /// </summary>
    /// <remarks>
    /// <c>section Main { partial 4  m { g4 | c4 d e f | } }</c> — the <c>partial</c> is a
    /// direct child of the SECTION, not of the part cell, so neither the file-level pass
    /// (<see cref="EmitScoreSettings"/>, which reads only <c>root.Members</c>) nor the cell's
    /// own items reached it and the twin lost the pickup. LilyPond then counts the pickup bar
    /// as a short bar and every bar line after it is a bar check failure — a twin that is
    /// silently a different piece, the same shape as the four holes before it.
    /// <para>
    /// Mirrors MeasureCollector: the same four directives, the same "first direct child wins",
    /// the same exclusion of a section that has INLINE MUSIC (such a section walks its own
    /// <c>key</c> as music — here that is <see cref="LooseSectionMusic"/>, which already
    /// yields them — so emitting them again would apply them twice), and the same order the
    /// collector applies them in (MeasureCollector.Form.cs: time, tempo, key, partial).
    /// </para>
    /// </remarks>
    private static IEnumerable<SyntaxNode> SectionHeaderMusic(SectionDeclarationSyntax section)
    {
        if (SectionHasInlineMusic(section))
            yield break;
        if (Semantics.SectionHeaders.FirstDirect<TimeSignatureSyntax>(section) is { } time) yield return time;
        if (Semantics.SectionHeaders.FirstDirect<TempoDeclarationSyntax>(section) is { } tempo) yield return tempo;
        if (Semantics.SectionHeaders.FirstDirect<KeySignatureSyntax>(section) is { } key) yield return key;
        if (Semantics.SectionHeaders.FirstDirect<PartialDeclarationSyntax>(section) is { } partial) yield return partial;
    }

    /// <summary>
    /// True when the section has a direct-child MUSIC node, as opposed to only directives and
    /// part / chord / lyric blocks — delegated to THE one spelling
    /// (MeasureCollector.SectionHasInlineMusic; this file's copy already agreed, the MIDI
    /// exporter's had drifted). ⚠️ The keyword and the braces are children too — the shared
    /// spelling skips tokens; dropping that once made every section look like it had inline
    /// music, so the header was never emitted.
    /// </summary>
    private static bool SectionHasInlineMusic(SectionDeclarationSyntax section)
        => Svg.Collector.MeasureCollector.SectionHasInlineMusic(section);

    /// <summary>
    /// The node holding a part block's music items — its LAST slot.
    /// </summary>
    /// <remarks>
    /// A <c>PartBlock</c> green node is <c>[partName, ..options, body]</c>
    /// (Syntax/InternalSyntax/GreenNodes.cs:685-694), so unlike a section its items are one
    /// level further down and <see cref="MusicItems"/> applied to the block itself would
    /// hand back the body as a single opaque node — which the emitter drops on the floor.
    /// </remarks>
    private static SyntaxNode? PartBlockBody(PartBlockSyntax? block)
    {
        if (block == null)
            return null;
        SyntaxNode? body = null;
        foreach (var child in EnumerateChildren(block))
            body = child;
        return body;
    }

    /// <summary>
    /// Flatten a form's items into the music stream IN DOCUMENT ORDER.
    /// </summary>
    /// <remarks>
    /// ⚠️ This replaced a walk that yielded only the section NAMES of the form's direct
    /// children (<c>FormSectionOrder</c>). Everything else a form can hold — and a form item
    /// has eight spellings (Parser.Form.cs ParseFormItem) — was dropped with no warning. Two of
    /// those drops were structural, and both produced a twin that COMPILES AND IS A DIFFERENT
    /// PIECE, which no warning and no snapshot can catch — only reading the .ly:
    /// <list type="bullet">
    /// <item><c>form { A break B }</c> — the <c>\break</c> never reached the twin, so
    /// LilyPond broke the line wherever its own spacing put it while Lily# broke it at B.</item>
    /// <item><c>form { A |: B :| dc A "A2" }</c> — a <c>|:</c> block is ONE child, so B
    /// (and every other section inside the repeat), the repeat bar lines and the D.C. all
    /// vanished. The twin was <c>A A</c>.</item>
    /// </list>
    /// <para>
    /// The repeat is NOT grouped here: <c>|:</c> / <c>:|</c> enter the stream as bar lines and
    /// <see cref="EmitInlineRepeat"/> groups them into <c>\repeat volta</c> / <c>\alternative</c>
    /// — which is exactly what <see cref="OrderedMusic"/>'s own comment always said would happen
    /// ("a repeat can span several sections, so grouping must happen AFTER this flattening").
    /// That path existed and worked; nothing ever fed it a bar line.
    /// </para>
    /// </remarks>
    private void AppendFormItems(
        IReadOnlyList<FormWalk.Item> items,
        Dictionary<string, (SectionDeclarationSyntax Section, SyntaxNode Container)> byName,
        List<SyntaxNode> result)
    {
        foreach (var item in items)
            AppendFormItem(item, byName, result);
    }

    private void AppendFormItem(
        FormWalk.Item item,
        Dictionary<string, (SectionDeclarationSyntax Section, SyntaxNode Container)> byName,
        List<SyntaxNode> result)
    {
        switch (item)
        {
            // The occurrence's display label, the collector's rule verbatim
            // (MeasureCollector.ResolveSectionLabel): the quoted label wins over the
            // section name, and an EMPTY label suppresses the mark. A silent `~`
            // reference hides the LABEL, not the music, so the twin carries the same
            // notes as a plain reference.
            case FormWalk.SectionRef s:
                AppendSection(s.Name, byName, result,
                    markLabel: Semantics.SectionLabelRule.LabelFor(
                        s.Silent, s.DisplayLabel, s.Name),
                    octaveOffset: s.OctaveOffset);
                break;

            case FormWalk.Repeat repeat:
                AppendRepeatBlock(repeat, byName, result);
                break;

            // A volta ending OUTSIDE a repeat block is just its section: there is no
            // \repeat for an \alternative to hang on. Its label rule mirrors
            // MeasureCollector.Form.cs (alt.DisplayLabel ?? name), and `~` hides it: the
            // tilde binds to the SECTION NAME in the grammar, so it hides what a plain
            // `~Name` hides.
            // ⚠️ THE MIRROR WAS TAKEN OF A BROKEN ARM (2026-08-25). This line and
            // CreateEnding's both stated that they mirrored MeasureCollector.Form.cs,
            // and that arm was the one page reader of four that had never been taught
            // IsSilent — so the citation carried the defect into the twin, twice.
            // ⇒ A "mirrors X" comment is a claim about X AT THE TIME IT WAS WRITTEN.
            case FormWalk.Ending lone:
                foreach (var s in lone.Sections)
                    AppendSection(s.Name, byName, result,
                        markLabel: Semantics.SectionLabelRule.LabelFor(s.Silent, s.DisplayLabel, s.Name),
                        octaveOffset: s.OctaveOffset);
                break;

            // The one-sided form-level ':|' flows through as the barline it is:
            // EmitInlineRepeat groups it exactly like an inline ':|'.
            case FormWalk.LoneRepeatEnd l:
                result.Add(l.Node);
                break;

            // `break` / `noBreak`, navigation marks and `@` marks are music where they
            // stand, and EmitItem already writes all three. Anything else a form can
            // hold (today only `_text`) goes through TOO, so that EmitItem's Skip
            // WARNS about it — filtering it here would put the drop back below the
            // waterline, which is the whole defect this method was rewritten for.
            // ⚠️ In a CHORD track only the breaks go through: a `\mark` or a navigation
            // mark written into the chord stream as well as the music would stand twice
            // at one moment, and the music stream already carries them; a form-level
            // `||` / `|.` is drawn by the staff, and writing it into \chordmode too changes
            // the chord variable of every book that ends its form with `|.` (two, in the
            // p364 sweep) for nothing. A form-level ':|:' never arrives here: FormWalk
            // reads it as a LoneRepeatEnd plus a Repeat (GroupDividerRepeats), which the
            // arms above carry into the chord track too.
            case FormWalk.Other o:
                if (!_chordTrack || o.Node is BreakSyntax)
                    result.Add(o.Node);
                break;
        }
    }

    /// <summary>Append one referenced section's header directives and music.</summary>
    /// <remarks>
    /// The headers come from the NAME-keyed registry, not from the chosen declaration —
    /// a split spelling (<c>section A { partial 8 }</c> beside the part's own
    /// <c>section A { … }</c>) keeps its header on the declaration that is never chosen.
    /// A declaration with inline music never registered, so its directives still arrive
    /// once, as its own loose music (<see cref="ContainerMusic"/>). The no-form fallback
    /// path keeps reading <see cref="SectionHeaderMusic"/> off each declaration in turn:
    /// there every declaration is played, header-only ones included, so the registry
    /// would hand the same directive to each of them.
    /// </remarks>
    private void AppendSection(
        string name,
        Dictionary<string, (SectionDeclarationSyntax Section, SyntaxNode Container)> byName,
        List<SyntaxNode> result,
        string? markLabel = null,
        int octaveOffset = 0)
    {
        // The printed play this is — counted before the lookup, as RepeatTiePlays counts it.
        int playIndex = _lpPlayIndex++;
        if (!byName.TryGetValue(name, out var entry))
        {
            AppendSilentPlay(name, result, markLabel, octaveOffset);
            return;
        }
        // A chord track's play of a section is its chord bars, nothing else: no play
        // marker (the \mark and the key/meter restores are the music stream's) and no
        // header directives — the key is read for the degree spelling instead.
        if (_chordTrack)
        {
            result.AddRange(ChordBars(entry.Container, ChordKeyFor(name), ChordPickupFor(name),
                SectionBarMeters(entry.Section, name)));
            result.AddRange(PaddingBars(entry.Container));
            return;
        }
        // The header directives of the name, in the collector's application order (time,
        // tempo, key, partial) — written as the play's own items, after its sentinel.
        var headers = _sectionHeaders.DirectivesOf(name);
        // The section-PLAY sentinel: the \mark and the score-key restore the collector
        // engraves at this boundary (see SectionPlayMarker). Planted inside volta ending
        // bodies too — the payload rides the marker's GREEN, so it survives
        // CreateEnding's green rebuild (before that, the endings' marks and restore
        // were a named remaining hole: the twin kept ending 1's key through ending 2
        // and carried no ending labels, while the page restores and boxes both).
        result.Add(new SectionPlayMarker(
            markLabel,
            headers?.Any(h => h is KeySignatureSyntax) == true,
            headers?.Any(h => h is TimeSignatureSyntax) == true,
            octaveOffset));
        if (headers != null)
            result.AddRange(headers);
        if (_repeatTiePlays.Contains(playIndex))
            result.Add(new RepeatTieMarker());
        // The score's meter (SectionMeterPlan): another part's `time` at a bar's start is written
        // in this voice too. LilyPond would apply it anyway (Timing is the Score's); writing it
        // keeps this stream's own meter — the length of its padding and empty bars — right.
        if (_meterPlan.IsEmpty)
            result.AddRange(ContainerMusic(entry.Container));
        else
            result.AddRange(_meterPlan.WithForeignChanges(name, entry.Container, ContainerMusic(entry.Container).ToList()));
        result.AddRange(PaddingBars(entry.Container));
    }

    /// <summary>
    /// A play of a section this voice writes NO block for: the section's canonical bars of
    /// silence, so the voice's next play stands beside the other voices' — what the page
    /// pads the staff with (MeasureCollector's section padding) and the MIDI walk and the
    /// MusicXML export count (SectionVoicePaddingExportTests). ⚠️ Until 2026-09-29 (HANDOFF
    /// §1.1 第662 ⑺) the play was skipped whole: `part bass { section A { … } section C { … } }`
    /// under `form { A B C }` wrote bass's C straight after its A, under the other
    /// parts' B — a different piece from the page, silently (Lab sessions/p674/probes/emptysec).
    /// </summary>
    /// <remarks>
    /// The same items <see cref="PaddingBars"/> writes for a SHORT play, from bar one: a music
    /// voice gets the play sentinel (its <c>\mark</c> and the key/meter restores, which the
    /// stream resets its bar clock on — every bare <c>|</c> after it is an empty bar, a spacer
    /// of the running meter, the section's header <c>partial</c> included) and the header
    /// directives, then one bare <c>|</c> a bar; a chord row a silent <c>\chordmode</c> bar a
    /// bar, of the header meter or the score's, the first as long as the header pickup. A
    /// section the index knows no voice of (a header-only declaration) has no bars to pad.
    /// </remarks>
    private void AppendSilentPlay(string name, List<SyntaxNode> result, string? markLabel, int octaveOffset)
    {
        if (!_sectionBars.Canonical.TryGetValue(name, out int bars) || bars <= 0)
            return;
        var headers = _sectionHeaders.DirectivesOf(name);
        if (_chordTrack)
        {
            var meter = _sectionHeaders.Times.GetValueOrDefault(name) is { IsSenzaMisura: false } t
                ? new Fraction(t.Beats, t.BeatType)
                : _bars.HomeMeter.Length;
            var pickup = ChordPickupFor(name);
            // Each bar as long as the MUSIC's bar there (SectionBarMeters — what a row that
            // writes the section is measured by): a `time` in the section's music is the
            // score's meter (HANDOFF §2 F-partmeter ⒜), and a row silent under a 3/4 bar wrote `s1`.
            var meters = _allSections.FirstOrDefault(s => s.SectionName == name) is { } declaration
                ? SectionBarMeters(declaration, name) : null;
            for (int i = 0; i < bars; i++)
            {
                var barMeter = meters is { Count: > 0 } ? meters[Math.Min(i, meters.Count - 1)] : meter;
                result.Add(new ChordBarMarker("s" + ChordModeDuration(i == 0 && pickup is { } p ? p : barMeter)));
                result.Add(CreateBarline(SyntaxKind.Bar, "|", 0, 0));
            }
            return;
        }
        result.Add(new SectionPlayMarker(
            markLabel,
            headers?.Any(h => h is KeySignatureSyntax) == true,
            headers?.Any(h => h is TimeSignatureSyntax) == true,
            octaveOffset));
        if (headers != null)
            result.AddRange(headers);
        for (int i = 0; i < bars; i++)
        {
            // The score's meter there (SectionMeterPlan): a `time` another part writes at
            // this bar's start is written here too, so the spacer is that long — LilyPond's
            // Timing is the Score's, and an `s1` under another staff's 3/4 failed its bar check.
            if (_meterPlan.ForeignChangeAt(name, i, null) is { } time)
                result.Add(time);
            result.Add(SectionPaddingBar(name, 0));
        }
    }

    // The meters other parts write into each section's bars (SectionMeterPlan), read once per Export.
    private Svg.Collector.SectionMeterPlan _meterPlan = Svg.Collector.SectionMeterPlan.Empty;

    /// <summary>A bare <c>|</c> the twin writes an empty bar of <paramref name="section"/>'s play at,
    /// remembered as that section's so the bar can be asked for its grace skip (<see cref="EmptyBarGracePad"/>).</summary>
    private BarlineSyntax SectionPaddingBar(string section, int position)
    {
        var bar = CreateBarline(SyntaxKind.Bar, "|", position, 0);
        _shared.PaddingBarSections[bar.Green] = section;
        return bar;
    }

    // The book's section voices (SectionBarCounts.BuildSemanticIndex), read once per Export.
    private Svg.Collector.SectionBarCounts.SemanticIndex _sectionBars = new();
    // The book's phrase bodies (SectionBarCounts.PhraseBodies) — what MeasureModel.Split
    // expands references through when a section's bars are measured (FirstBarsLength).
    private IReadOnlyDictionary<string, SyntaxNode> _phraseBodies = new Dictionary<string, SyntaxNode>();

    /// <summary>
    /// The silent bars that bring one voice's play of a section up to the section's
    /// canonical bar count — what the page pads the short staff with (MeasureCollector's
    /// section padding, spacer rests), so the twin's voices stay side by side: without it a
    /// one-bar melody A beside a two-bar chord row A put B's notes under A's second chord
    /// (scratch/ベースタブLy/tooLongChords.lys), and a one-bar melody A beside a two-bar bass
    /// A put melody's B under bass's A (scratch/p363/pm-two-parts.lys; MEASURED 2026-09-10).
    /// </summary>
    /// <remarks>
    /// Music voices: bare <c>|</c> nodes, which <see cref="EmitMusicStream"/>'s empty-bar rule
    /// writes as <c>s1 |</c> each — the spelling an author's own <c>| |</c> gets — with one
    /// extra when the voice's last bar is still open (that first <c>|</c> only closes it;
    /// the index says). Chord rows: a silent <c>\chordmode</c> bar per missing bar, the
    /// spelling <see cref="ChordBars"/> gives an empty bar, after closing an open one. The
    /// count is the SEMANTIC one (<c>R1*4</c> is four bars, a repeat its played length) —
    /// the first cut used the page's syntactic count and wrote 48 spurious bars after
    /// canon-in-d's <c>repeat unfold 13</c>.
    /// </remarks>
    /// <summary>The section a chord container belongs to: the container itself when it is
    /// a by-part track's inner section, else the section enclosing the flat block; null
    /// for a block at top level.</summary>
    private static SectionDeclarationSyntax? SectionOf(SyntaxNode container)
    {
        if (container is SectionDeclarationSyntax own)
            return own;
        for (var n = container.Parent; n != null; n = n.Parent)
            if (n is SectionDeclarationSyntax s)
                return s;
        return null;
    }

    private IEnumerable<SyntaxNode> PaddingBars(SyntaxNode container)
    {
        int missing = _sectionBars.Missing(container, out bool open);
        if (missing <= 0)
            yield break;
        int position = container.Position;
        int written = WrittenBars(container);
        if (_chordTrack)
        {
            if (open)
                yield return CreateBarline(SyntaxKind.Bar, "|", position, 0);
            // The missing bars are as long as the MUSIC's last bar of the section
            // (SectionBarMeters: the header's meter, moved by any `time` the music writes) —
            // not the score's, which a row short of a 3/4 section was padded with until
            // 2026-10-03 (`s1` beside `c2.`, the same family as the bars it did write).
            var meters = SectionOf(container) is { } section
                ? SectionBarMeters(section, section.SectionName) : null;
            for (int i = 0; i < missing; i++)
            {
                var meter = meters is { Count: > 0 }
                    ? meters[Math.Min(written + i, meters.Count - 1)] : _bars.HomeMeter.Length;
                yield return new ChordBarMarker("s" + ChordModeDuration(meter));
                yield return CreateBarline(SyntaxKind.Bar, "|", position, 0);
            }
            yield break;
        }
        // A voice whose last bar is CLOSED gets `missing` empty bars, each a bare `|` the
        // stream writes as a spacer — which the stream only does when nothing took time
        // since the last boundary. A phrase reference counts as time there (a bare `|` after
        // one closes a bar on the page too: `mel | e'1 |` is two bars with `mel = { c'1 | }`),
        // so a part written as phrase references wrote its first padding bar as a bare bar
        // CHECK and lost it: `rh { p1 p1 }` padded to four bars drew three in the twin, and
        // the fixture grammar-tour's right hand ran a bar early from section B on (Lab
        // sessions/p585). The marker states the index's answer to the stream.
        if (!open)
            yield return new ClosedBarMarker();
        var name = SectionOf(container)?.SectionName;
        for (int i = 0; i < missing + (open ? 1 : 0); i++)
        {
            // The bar this `|` writes empty (with an open last bar, the first `|` only closes
            // it) is in the score's meter there (SectionMeterPlan) — see AppendSilentPlay.
            int bar = written + i - (open ? 1 : 0);
            if (name != null && bar >= written && _meterPlan.ForeignChangeAt(name, bar, container) is { } time)
                yield return time;
            yield return name != null ? SectionPaddingBar(name, position) : CreateBarline(SyntaxKind.Bar, "|", position, 0);
        }
    }

    /// <summary>The bars a voice container writes (the semantic count), 0 when the index
    /// does not hold it.</summary>
    private int WrittenBars(SyntaxNode container)
    {
        if (container is MusicBlockSyntax { Parent: PartBlockSyntax block })
            container = block;
        return _sectionBars.ByContainer.TryGetValue(container, out var voice) ? voice.Bars : 0;
    }

    /// <summary>
    /// Flatten <c>|: … :|</c> into bar lines plus the sections between them.
    /// </summary>
    /// <remarks>
    /// Document order is kept verbatim, including endings that sit BEFORE the <c>:|</c>
    /// (<c>|: … [1. D] :| [2. Outro]</c> — the repeat bar belongs between the endings), because
    /// <see cref="EmitInlineRepeat"/> collects every ending it meets and keeps scanning past the
    /// <c>:|</c> for more. The play count rides the closing bar line the way an inline
    /// <c>:|*N</c> does, since that is where <see cref="EmitInlineRepeat"/> reads it.
    /// <para>
    /// Mirrors MeasureCollector.ProcessRepeatBlock, including <c>:|:</c> — one written divider
    /// is two bar lines (<c>:|</c> then <c>|:</c>), so <c>|: B :|: C :|</c> is
    /// <c>|: B :| |: C :|</c>. ⚠️ That is the one item where the two walks MUST agree: expand it
    /// on one side only and the twin repeats a different number of bars than Lily# does.
    /// </para>
    /// </remarks>
    private void AppendRepeatBlock(
        FormWalk.Repeat repeat,
        Dictionary<string, (SectionDeclarationSyntax Section, SyntaxNode Container)> byName,
        List<SyntaxNode> result)
    {
        // The :|*N play count is read once, by FormWalk.PlayCount (default 2).
        int playCount = repeat.PlayCount;

        for (int ci = 0; ci < repeat.Children.Count; ci++)
        {
            var child = repeat.Children[ci];
            switch (child)
            {
                case FormWalk.RepeatStart { Token: var open }:
                    result.Add(CreateBarline(SyntaxKind.RepeatStartBar, "|:", open.Position, 0));
                    break;

                case FormWalk.RepeatEnd { Token: var close }:
                    result.Add(CreateBarline(SyntaxKind.RepeatEndBar, ":|", close.Position, playCount));
                    break;

                case FormWalk.BothBar { Token: var both }:
                    result.Add(CreateBarline(SyntaxKind.RepeatEndBar, ":|", both.Position, playCount));
                    result.Add(CreateBarline(SyntaxKind.RepeatStartBar, "|:", both.Position, 0));
                    break;

                case FormWalk.Ending ending when ending.Sections.Any(s => byName.ContainsKey(s.Name)):
                    // An ending a ':|' follows ends on LilyPond's ":|." bar, whose glyph
                    // allows the hook by itself; the one after the last ':|' does not.
                    result.Add(CreateEnding(ending, byName,
                        lastEnding: !(ci + 1 < repeat.Children.Count
                                      && repeat.Children[ci + 1] is FormWalk.RepeatEnd or FormWalk.BothBar)));
                    break;

                default:
                    AppendFormItem(child, byName, result);
                    break;
            }
        }
    }

    /// <summary>
    /// A form ending (<c>[1. D]</c>, <c>[1. C D]</c>) as the inline ending node the emitter
    /// groups — one alternative holding every section of the ending, in order.
    /// </summary>
    /// <remarks>
    /// The two spellings differ only in where the music lives: an inline volta HOLDS its items,
    /// a form ending NAMES a section that holds them. Rebuilding the inline node around the
    /// section's own green nodes lets <see cref="EmitInlineRepeat"/> stay the single place that
    /// knows how <c>\alternative</c> is written — the alternative was to teach it a second node
    /// shape, i.e. a second spelling of the same thing (the defect this file keeps finding).
    /// ⚠️ The rebuilt node carries the ENDING's source position, not the section's; nothing in
    /// the .ly reads positions, but a warning raised on one of these items points at the form.
    /// </remarks>
    private InlineVoltaSyntax CreateEnding(
        FormWalk.Ending written,
        Dictionary<string, (SectionDeclarationSyntax Section, SyntaxNode Container)> byName,
        bool lastEnding)
    {
        var ending = written.Node;
        var items = new List<SyntaxNode>();
        // Each section's label rule mirrors MeasureCollector.Form.cs's alternative arm
        // (DisplayLabel ?? name, hidden by `~`), like the outside-a-repeat
        // Ending case. ⚠️ The tilde takes the LABEL and not the ending: the volta
        // green built below is emitted whatever the tildes say, because a repeat with no
        // bracket is spelled by writing no ending (`|: A :|`). See the note on that case.
        int shapeAt = -1;
        foreach (var s in written.Sections)
        {
            int before = items.Count;
            AppendSection(s.Name, byName, items,
                markLabel: Semantics.SectionLabelRule.LabelFor(s.Silent, s.DisplayLabel, s.Name),
                octaveOffset: s.OctaveOffset);
            if (shapeAt < 0 && items.Count > before)
            {
                // After the first play's marker and header directives: a `\time` there is
                // the meter a `voltaBracket N` counts its bars in.
                shapeAt = before;
                while (shapeAt < items.Count && items[shapeAt] is { Green: SectionPlayGreen }
                           or KeySignatureSyntax or TimeSignatureSyntax or TempoDeclarationSyntax
                           or ClefDeclarationSyntax)
                    shapeAt++;
            }
        }
        if (VoltaShapeFor(written, lastEnding) is { } shape)
            items.Insert(Math.Max(0, shapeAt), shape);

        var green = new InternalSyntax.InlineVoltaGreen(
            new InternalSyntax.SyntaxToken(SyntaxKind.OpenBracket, "["),
            new InternalSyntax.SyntaxToken(SyntaxKind.IntegerLiteral, ending.Number.Text),
            ending.Separator is { } sep ? new InternalSyntax.SyntaxToken(sep.Kind, sep.Text) : null,
            ending.EndNumber is { } end ? new InternalSyntax.SyntaxToken(end.Kind, end.Text) : null,
            // A list's further numbers, each with its comma: [1,3,5. …].
            [.. ending.MorePasses.SelectMany(n => new[]
            {
                new InternalSyntax.SyntaxToken(SyntaxKind.Comma, ","),
                new InternalSyntax.SyntaxToken(SyntaxKind.IntegerLiteral, n.Text),
            })],
            new InternalSyntax.SyntaxToken(SyntaxKind.Dot, "."),
            [.. items.Select(n => n.Green)],
            new InternalSyntax.SyntaxToken(SyntaxKind.CloseBracket, "]"));

        return new InlineVoltaSyntax(green, null, ending.Position);
    }

    /// <summary>
    /// The overrides that give an ending's LilyPond bracket the page's end shape and length
    /// (owner's design 2026-09-28; <see cref="Svg.Collector.MeasureCollector"/>'s
    /// <c>EndingBracket</c> is the page's reading of the same three settings), or null when
    /// LilyPond's default already draws it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/volta-engraver.cc:394-418 Volta_engraver::acknowledge_bar_line — with
    ///   the end of the timestep after it, the right <c>edge-height</c> is zeroed unless the
    ///   bar the bracket ends on is in the allow-volta-hook list (":|." "|." and kin).
    /// LILYPOND-REF: scm/bar-line.scm:1130-1133 volta-bracket::calc-hook-visibility — that list's
    ///   reader. So a first ending (it ends on ":|.") hooks by default and a last one on a
    ///   plain bar does not: a hooked LAST ending re-sets the hook after line breaking, and an
    ///   open one (<c>-]</c>) zeroes it up front, which the engraver leaves alone.
    /// LILYPOND-REF: lily/volta-engraver.cc:428-533 Volta_engraver::stop_translation_timestep —
    ///   <c>VoltaBracket.musical-length</c> ends the bracket that many whole notes after its
    ///   start (<c>voltaBracket N</c>: N bars of the meter the ending opens in).
    /// LILYPOND-REF: lily/volta-bracket.cc:115-142 Volta_bracket_interface::modify_edge_height —
    ///   a broken bracket's first piece has no right hook; <c>voltaBracket line</c> kills every
    ///   later piece.
    /// </remarks>
    private SyntaxNode? VoltaShapeFor(FormWalk.Ending written, bool lastEnding)
    {
        var ending = written.Node;
        var length = ending.LengthUnder(_layoutPlan.VoltaBracket);
        int bars = 0;
        foreach (var s in written.Sections)
            if (_sectionBars.Canonical.TryGetValue(s.Name, out int n))
                bars += n;
        int cutBars = length.Mode == Semantics.VoltaBracketLengthMode.Bars && length.Bars < bars
            ? length.Bars : 0;
        bool line = length.Mode == Semantics.VoltaBracketLengthMode.Line;
        bool hooked = ending.EndsHooked && cutBars == 0;
        if (cutBars == 0 && !line && hooked && !lastEnding)
            return null; // a hooked ending before its ':|' — LilyPond's own picture
        return new VoltaShapeMarker(new VoltaShapeGreen(hooked, lastEnding, line, cutBars,
            [.. written.Sections.Select(s => s.Name)]));
    }

    /// <summary>The LilyPond overrides a <see cref="VoltaShapeGreen"/> asks for, in the meter
    /// the stream is in where the ending starts.</summary>
    private string EmitVoltaShape(VoltaShapeGreen v)
    {
        const string once = "\\once \\override Score.VoltaBracket.";
        const string openEnd = once + "edge-height = #'(2.0 . 0.0)";
        var parts = new List<string>();
        if (v.CutBars > 0)
        {
            // The ending's first N bars, in whole notes — the bars' own lengths.
            var length = FirstBarsLength(v.Sections, v.CutBars);
            parts.Add(once + $"musical-length = #(ly:make-moment {length.Numerator}/{length.Denominator})");
            parts.Add(openEnd);
            return string.Join(" ", parts);
        }
        // ⚠️ The hook is re-set BEFORE line breaking, not after: the bracket's stencil is
        // computed while the lines are being broken (its skyline), and an after-line-breaking
        // callback ran after it — MEASURED on LilyPond 2.26.0, the stencil kept the engraver's
        // zeroed right edge. The broken pieces inherit the property, and the print zeroes
        // the inner ends itself (Volta_bracket_interface::modify_edge_height).
        if (v.Hooked && (v.LastEnding || v.FirstSystemOnly))
            parts.Add(once + "before-line-breaking = #(lambda (grob) "
                + "(ly:grob-set-property! grob 'edge-height '(2.0 . 2.0)))");
        // Killing the later pieces can only happen once they exist; a dead grob is not
        // output whatever its stencil was.
        if (v.FirstSystemOnly)
            parts.Add(once + "after-line-breaking = #(lambda (grob) (let ((pieces "
                + "(ly:spanner-broken-into (ly:grob-original grob)))) (if (and (pair? pieces) "
                + "(not (eq? grob (car pieces)))) (ly:grob-suicide! grob))))");
        if (!v.Hooked)
            parts.Add(openEnd);
        return string.Join(" ", parts);
    }

    /// <summary>
    /// The musical length of an ending's first <paramref name="bars"/> bars — where the page's
    /// <c>voltaBracket N</c> ends its bracket (<c>VoltaBracketLength.LastBar</c> counts bars),
    /// as LilyPond's <c>musical-length</c> must be told it: the bars' OWN lengths
    /// (<see cref="MeasureModel.Split"/>, the validators' bar model, over the section's longest
    /// music voice), summed section by section for an ending of several. So a <c>time</c>
    /// change inside the ending, or a bar shorter than its meter, ends the twin's bracket on
    /// the bar the page ends it on. ⚠️ Until 2026-09-29 (第663 ⒀) this was N bars of the
    /// meter the ending OPENS in — right for <c>b'1 | b'1 |</c>, a bar short for
    /// <c>b'1 | time 3/4 b'2. |</c>. A section with no music voice indexed (a chord-row
    /// section) still counts N bars of the opening meter, as before.
    /// </summary>
    private Fraction FirstBarsLength(IReadOnlyList<string> sections, int bars)
    {
        var meter = _bars.MeterLength;
        var total = Fraction.Zero;
        int left = bars;
        foreach (string name in sections)
        {
            if (left <= 0)
                break;
            Svg.Collector.SectionBarCounts.SemanticVoice? longest = null;
            foreach (var voice in _sectionBars.ByContainer.Values)
                if (voice.SectionName == name && !voice.IsChords && (longest == null || voice.Bars > longest.Bars))
                    longest = voice;
            if (longest == null)
            {
                // No music voice: N bars of the opening meter, as the section's bar count is
                // the canonical one (a chord row's bars are the meter long).
                int count = Math.Min(left, _sectionBars.Canonical.TryGetValue(name, out int n) ? n : left);
                total += meter * new Fraction(count);
                left -= count;
                continue;
            }
            foreach (var bar in MeasureModel.Split(longest.Container, _phraseBodies, meter))
            {
                if (left <= 0)
                    break;
                total += bar.Duration;
                left--;
            }
        }
        return total;
    }

    /// <summary>
    /// A bar line node the source never wrote — the <c>|:</c> / <c>:|</c> a form's repeat block
    /// spells with its own tokens. <paramref name="playCount"/> of 0 leaves the count off.
    /// </summary>
    private static BarlineSyntax CreateBarline(SyntaxKind kind, string text, int position, int playCount)
    {
        var green = new InternalSyntax.BarlineGreen(
            new InternalSyntax.SyntaxToken(kind, text),
            playCount > 2 ? new InternalSyntax.SyntaxToken(SyntaxKind.Asterisk, "*") : null,
            playCount > 2 ? new InternalSyntax.SyntaxToken(SyntaxKind.IntegerLiteral, playCount.ToString()) : null);
        return new BarlineSyntax(green, null, position);
    }

    private List<SyntaxNode> TopLevelMusic(CompilationUnitSyntax root)
    {
        var result = new List<SyntaxNode>();
        foreach (var m in root.Members)
            if (IsMusicItem(m))
                result.Add(m);
        return result;
    }

    // ---- Music stream (with repeat grouping) -------------------------------

    /// <summary>
    /// One section PLAY: the boxed section label and — when the running key differs
    /// from the score's home key and no section-header key follows — the <c>\key</c>
    /// that restores it. The two events the twin used to lose silently (the exporter's
    /// 7th and 8th silent-drop holes; reported 2026-08-13,
    /// scratch/ベースタブLy/Untitled-3.lys — the twin kept the modulated key to the end
    /// and carried no marks). Mirrors MeasureCollector's boundary
    /// (Form.cs: header key wins over the score-key revert, else-if), reading the same
    /// running-key state EmitKey advances; the restore re-emits the home DECLARATION
    /// node so mode/spelling come from the source.
    /// </summary>
    private string EmitSectionPlay(SectionPlayGreen sp)
    {
        // ⚠️ A SECTION BOUNDARY REOPENS LILY#'s FRAME at the part's anchor
        // (OctaveContext.ResetForSection: CurrentOctave = InitialOctave, LastPitchName = 'c')
        // and LilyPond's `\relative` chain knows nothing about it — so only the LILY# side
        // moves here and the next pitch writes the difference into its own marks. Exactly
        // the shape EmitClef uses for a mid-bar clef, and the third spelling of one rule:
        // the collector resets, the MIDI and the MusicXML reset, and the twin compensates.
        // MEASURED 2026-08-17 on `section A { c'4 d e f } section B { g'4 f e d }`: the page
        // prints G4 to open B, and the twin handed LilyPond a `g'` that reads G6 — the twin
        // was a different piece from the bar the boundary opens, on every book with two
        // sections and a frame-moving first note.
        // ⚠️ AND THE REFERENCE'S OWN MARKS MOVE THAT REOPENING (`~B'`, 2026-08-31): the
        // collector re-anchors the play a whole octave up (OctaveContext.ResetForSection),
        // so the twin has to reopen at the same place or hand LilyPond a different piece.
        // The two modes need different halves of the same sentence, exactly as a marked
        // PHRASE reference does: relative moves the frame, absolute has no frame and moves
        // the marks each pitch writes (EmitVariableReference emits a nested \fixed for its
        // half; a section's music is INLINED into this stream, so there is no block to nest
        // and the shift rides on the emitter instead).
        // ⚠️ The offset is REMEMBERED for the whole play, not applied once: a phrase body
        // inside the section opens its own fresh frame (EmitVariableReference), and that
        // frame is the SECTION's anchor. Kept in one field for both modes — the relative arm
        // reads it below and in EmitVariableReference, the absolute arm at every pitch.
        _sectionOctaveOffset = sp.OctaveOffset;
        if (!_octaveAbsolute)
        {
            _lysStep = 0;
            _lysOctave = _anchorOctave + _sectionOctaveOffset;
        }
        // ⚠️ THE DURATION REOPENS HERE TOO. A section's first unwritten duration is a
        // QUARTER in Lily# (owner decision 2026-09-04, HANDOFF §3 — the section is a
        // reusable unit and "the previous section" is not one thing;
        // MeasureCollector.Form.cs's section prologue resets _defaultDuration), while
        // LilyPond's parser carries the last duration READ across the boundary
        // (LILYPOND-REF: lily/parser.yy:3503-3515 optional_notemode_duration —
        // default_duration_ is parser state and knows no section). So when the last
        // written value is not a plain quarter, the boundary's first unwritten event
        // writes the quarter out — otherwise the twin plays that note at the previous
        // section's value. MEASURED (session 350, the owner's ぐるぐるワンダーランド and
        // Lambada Complicada through LilyPond 2.26.0): a section opening `aes aes' …`
        // after `<des' fes>1` read as two wholes and failed the bar check at 1/2.
        if (_lastWrittenValue != "4" || _lastWrittenDots > 0)
        {
            _lastWrittenValue = "4";
            _lastWrittenDots = 0;
            _forceNextDuration = true;
        }

        var parts = new List<string>(3);
        // Whatever an earlier head still holds is written first (two plays back to back with
        // nothing between them), and THIS head is what stands now, before its restores.
        string earlierHeld = FlushSectionHead();
        if (earlierHeld.Length > 0)
            parts.Add(earlierHeld);
        var head = (TimeTextInForce, _bars.SenzaMisura, _keySharps, _tonic);
        int restoresAt = parts.Count;
        // ⚠️ THE METER REVERTS HERE TOO, and this arm is the twin of the key one below.
        // A section that states no `time` of its own opens at the SCORE meter, so a
        // mid-section change in a PRIOR section (or in an earlier play of this one) must
        // not leak across — MeasureCollector.ProcessSectionPrologue reverts it against the
        // per-voice snapshot and the page draws the restored signature. Until 2026-08-31
        // this carrier answered only the key question, so `section A { … time 3/4 … }
        // section B { c'4 d e f | }` handed LilyPond a 3/4 bar holding four quarters.
        if (!sp.HasHeaderTime
            && (!_bars.Meter.SamePair(_bars.HomeMeter) || _bars.SenzaMisura != _bars.HomeSenzaMisura))
        {
            if (_homeTimeNode != null)
            {
                parts.Add(EmitTime(_homeTimeNode)); // EmitTime advances the running meter
            }
            else
            {
                parts.Add(_bars.SenzaMisura
                    ? "\\cadenzaOff \\time 4/4" + CadenzaReturnPartial(Fraction.Whole)
                    : "\\time 4/4");
                _bars.SetMeter(new Semantics.Meter(4, 4));
            }
            // HELD, not written: a `time` at the play's head that states the meter in force
            // before this restore cancels it (EmitItem's head arm, MeasureBuilder.SectionHead).
            _heldTimeRestore = parts[^1];
            parts.RemoveAt(parts.Count - 1);
        }
        if (!sp.HasHeaderKey && (_keySharps != _restoreKeySharps || _tonic != _homeTonic))
        {
            if ((_partHeaderKeyNode ?? _homeKeyNode) is { } restoreKey)
            {
                parts.Add(EmitKey(restoreKey)); // EmitKey advances _keySharps/_tonic
                // …but the ambient (phrase auto-transpose) key is the file's home on the page.
                _tonic = _homeTonic;
            }
            else
            {
                if (!_drumMode)   // no key in \drummode (EmitKey's remark)
                    parts.Add("\\key c \\major");
                _keySharps = 0;
                _tonic = KeyTonic.CMajor;
            }
            // HELD as the meter's restore is.
            if (parts.Count > restoresAt)
            {
                _heldKeyRestore = parts[^1];
                parts.RemoveAt(parts.Count - 1);
            }
        }
        _sectionHead = head;
        // `layout { sectionLabels … }`: the page engraves no section name under `none`, so the
        // twin writes none either — the two pictures are the same picture or the twin is not
        // one. Under `plain` the twin drops the `\box` and writes the bare string, which is
        // what LilyPond's own SectionLabel grob draws; the frame is the Lily#-own part and
        // `\box` is only how the twin reaches it.
        // HELD with the restores, and written after them, as it always was: the restores, the
        // mark, then the section's own directives (EmitItem's head arm keeps that order).
        if (sp.MarkLabel is { Length: > 0 } label
            && _layoutPlan.SectionLabels != Semantics.SectionLabelStyle.None)
            _heldMark = _layoutPlan.SectionLabels == Semantics.SectionLabelStyle.Plain
                ? "\\mark \\markup \"" + Escape(label) + "\""
                : "\\mark \\markup \\box \"" + Escape(label) + "\"";
        return string.Join(" ", parts);
    }

    // The form this twin writes: the caller's choice, else the primary one. ⚠️ The reading
    // itself moved to ScoreForms — this used to match `main` case-INSENSITIVELY while the
    // MIDI and MusicXML exporters matched it exactly, which is two answers to one question.
    private FormDeclarationSyntax? PrimaryForm(CompilationUnitSyntax root)
        => Form ?? _scoreForm ?? LilySharp.Core.Semantics.ScoreForms.Primary(root);

    /// <summary>The form <see cref="Score"/> renders, read once per export; null when no
    /// score was named or its form is unknown.</summary>
    private FormDeclarationSyntax? _scoreForm;

    // The music items directly inside a container (section/part/block): every
    // non-token child (notes, rests, barlines, breaks, key/time/tempo, …).
    private static IEnumerable<SyntaxNode> MusicItems(SyntaxNode container)
    {
        foreach (var child in EnumerateChildren(container))
            if (IsMusicItem(child))
                yield return child;
    }

    /// <summary>
    /// The music of a container chosen by <see cref="OrderedMusic"/> — its items, except that
    /// a section standing in for the single-part shorthand contributes only its LOOSE music.
    /// </summary>
    private IEnumerable<SyntaxNode> ContainerMusic(SyntaxNode container)
        => _looseSections.Contains(container) ? LooseSectionMusic(container) : MusicItems(container);

    /// <summary>
    /// A top-level section's own direct music — the "single-part shorthand", where the lone
    /// part's notes are written into the section with no cell around them.
    /// </summary>
    /// <remarks>
    /// Narrower than <see cref="MusicItems"/> on purpose: a section can hold OTHER parts' cells
    /// and its own track blocks (lyrics/chords) beside the loose music, and those belong to
    /// somebody else. LILYSHARP-OWN — LilyPond has no section/part split to be loose in.
    /// <para>
    /// ⚠️ It is MeasureCollector's <c>IsCollectableMusicNode</c> MINUS the three grob
    /// directives (override / revert / once) and PLUS three nodes that only an exporter needs:
    /// a phrase <c>VariableReference</c> (which <see cref="EmitPhraseReference"/> expands where
    /// it stands), and <c>Dynamic</c> / <c>Articulation</c>, which the collector reaches by
    /// attachment rather than as loose children. The overrides are left out because
    /// <see cref="Skip"/> is still all this exporter can do with them — listing them here would
    /// only move a silent drop into a warning, and the two sets would then differ for a reason
    /// nobody had written down.
    /// </para>
    /// </remarks>
    private static IEnumerable<SyntaxNode> LooseSectionMusic(SyntaxNode section)
    {
        foreach (var child in EnumerateChildren(section))
        {
            if (child is NoteSyntax or DrumNoteSyntax or RestSyntax or ChordSyntax
                or ChordRepetitionSyntax or SlashNoteSyntax or BareDurationSyntax
                or ArpeggioSyntax or BarlineSyntax or BreakSyntax or TieSyntax or SlurSyntax
                or BeamMarkerSyntax or GraceExpressionSyntax or TupletExpressionSyntax
                or RepeatExpressionSyntax or ParallelExpressionSyntax or InlineVoltaSyntax
                or MusicMarkSyntax or NavigationMarkSyntax or ClefDeclarationSyntax
                or OctaveDirectiveSyntax or KeySignatureSyntax or TimeSignatureSyntax
                or TempoDeclarationSyntax or PartialDeclarationSyntax
                or VariableReferenceSyntax or DynamicSyntax or ArticulationSyntax)
            {
                yield return child;
            }
        }
    }

    private static bool IsMusicItem(SyntaxNode n) => n is not SyntaxTokenNode
        && n is not SectionDeclarationSyntax; // sections are flattened separately

    private static IEnumerable<SyntaxNode> EnumerateChildren(SyntaxNode node)
    {
        for (int i = 0; i < node.SlotCount; i++)
            if (node.GetChild(i) is SyntaxNode child)
                yield return child;
    }

    /// <summary>The part a <c>staff</c> or <c>tab</c> item renders, read the way the page
    /// reads it — the node for a tab (<see cref="TabRenderSyntax.PartToken"/>: of two words
    /// the first when it is a declared part, else the last) and the rename's reading for a
    /// staff (<see cref="Editing.PartReferenceFinder.StaffPartToken"/>: selectors, tilde and
    /// display name cut, a clef word before the name skipped).</summary>
    /// <remarks>
    /// Until 2026-10-03 this took the first IDENTIFIER after the keyword for itself, which
    /// named <c>full</c> as the part of <c>tab bass as full</c> (<c>bass</c> lexes as a clef
    /// word) — a TabStaff of a part that does not exist, in guitar tuning, holding only the
    /// form's road-map marks (Lab probes/complex-lys/06) — and, once any bare word may name a
    /// part, would have named the clef of <c>staff treble p</c>. One reading per item now,
    /// and the twin can no longer disagree with the page about which part a row shows.
    /// </remarks>
    private static string? RenderPartName(SyntaxNode renderItem) => renderItem switch
    {
        TabRenderSyntax tab => tab.PartToken?.Text,
        StaffRenderSyntax staff => Editing.PartReferenceFinder.StaffPartToken(staff)?.Text,
        _ => null,
    };
}
