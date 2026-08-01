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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.LilyPond;

/// <summary>
/// Exports a Lily# syntax tree back to a standalone LilyPond <c>.ly</c> source.
///
/// This is a TRANSPILER, not a re-derivation: pitch tokens (letter, accidental,
/// and the <c>'</c>/<c>,</c> octave marks) are copied through VERBATIM so the
/// octave the author wrote in the <c>.lys</c> is preserved byte-for-byte. To keep
/// those verbatim marks pitch-correct in real LilyPond, the music is wrapped in a
/// reference that matches Lily#'s own octave convention:
///   • <c>octave absolute</c> input → <c>\fixed c' { … }</c> (bare <c>c</c> = middle C;
///     absolute mode is anchored at middle C whatever the clef — OctaveContext's
///     "clef default is deliberately NOT used here")
///   • relative input (the default) → <c>\relative</c> at THE PART'S OWN anchor, which is
///     not always <c>c'</c>: Lily#'s relative anchor is the part's default octave, and that
///     follows the clef (InstrumentDefaults.GetDefaultOctave — bass/alto/tenor anchor at
///     octave 3, i.e. LilyPond <c>c</c>), with an explicit <c>octave N</c> part property
///     overriding it. ⚠️ This file used to write <c>\relative c'</c> unconditionally and
///     say so in this very comment, which made every non-treble part export AN OCTAVE HIGH
///     — 54 of the 204 fixtures declare a bass, alto or tenor clef.
///
/// It reproduces the MUSIC and the staff/tab structure the score declares; it does
/// NOT reconstruct anything the <c>.lys</c> does not hold (a hand <c>.ly</c>'s
/// comments, multiple <c>\book</c> variants, custom definitions).
/// </summary>
public sealed class LilyPondExporter
{
    private const string LilyPondVersion = "2.26.0";

    private readonly StringBuilder _sb = new();
    private readonly List<string> _warnings = new();
    private bool _octaveAbsolute; // false = relative (Lily#'s default)

    /// <summary>
    /// The octave the part being emitted anchors its relative pitches to — Lily#'s
    /// "default octave", 4 for treble.
    /// </summary>
    /// <remarks>
    /// It is state rather than a parameter because the two places that spell an anchor are
    /// not adjacent: the part's own wrapper (<see cref="EmitPartVariable"/>) and every
    /// nested <c>\relative</c> a phrase reference opens (<see cref="ReferencePitch"/>).
    /// Both have to move together or a bass part's phrases land an octave off its own
    /// wrapper, which is worse than both being wrong the same way.
    /// ⚠️ Sub-exporters must inherit it — see the phrase-body buffer.
    /// </remarks>
    private int _anchorOctave = InstrumentDefaults.GetDefaultOctave(ClefType.Treble);

    /// <summary>
    /// Phrase (and variable) bodies by name, so a bare reference in a section can be
    /// expanded in place. Shared with the sub-exporters that emit nested bodies.
    /// </summary>
    private Dictionary<string, SyntaxNode> _phrases = new(StringComparer.Ordinal);

    /// <summary>References being expanded right now — the cycle guard, the same one
    /// MusicXmlExporter and MidiExporter keep for the same reason.</summary>
    private HashSet<string> _activePhrases = new(StringComparer.Ordinal);

    /// <summary>Sections standing in for the single-part shorthand, so
    /// <see cref="ContainerMusic"/> knows to take only their LOOSE music and leave any other
    /// part's cell alone. Identity, not name: the same section can be a container here and a
    /// mere holder of somebody else's cell for the next part.</summary>
    private readonly HashSet<SyntaxNode> _looseSections = new();

    /// <summary>Diagnostics collected while exporting (e.g. constructs dropped
    /// because they are deprecated or out of scope). Not fatal.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Exports the tree and returns the complete <c>.ly</c> text.</summary>
    public string Export(SyntaxTree tree)
    {
        var root = tree.GetRoot();

        // Octave mode is a file-level directive; default is relative (Lily#'s default).
        var octaveDir = root.DescendantNodes<OctaveDirectiveSyntax>().FirstOrDefault();
        _octaveAbsolute = octaveDir?.IsAbsolute ?? false;

        CollectPhrases(root);

        EmitHeader(root);

        var parts = root.DescendantNodes<PartDeclarationSyntax>().ToList();
        var sections = root.DescendantNodes<SectionDeclarationSyntax>().ToList();
        var form = PrimaryForm(root);
        var render = root.DescendantNodes<RenderDeclarationSyntax>().FirstOrDefault();

        // One music variable per part. A part-major score keeps its sections inside
        // the part block; the form orders them.
        var partVars = new Dictionary<string, string>(StringComparer.Ordinal);
        var names = PartNames(parts, render);
        if (names.Count > 0)
        {
            foreach (string name in names)
            {
                var part = parts.FirstOrDefault(p => p.Name.Text == name);
                string varName = SanitizeVar(name);
                partVars[name] = varName;
                // An undeclared part has no clef property to anchor to, so it takes the
                // same default the collector gives it (RenderSpecParser.GetPartClef returns
                // null → ClefType.Treble → octave 4).
                _anchorOctave = part != null
                    ? AnchorOctaveOf(part)
                    : InstrumentDefaults.GetDefaultOctave(ClefType.Treble);
                EmitPartVariable(varName, OrderedMusic(name, part, form, sections), root);
            }
        }
        else
        {
            // No part at all — neither declared nor named by a score: treat the whole
            // file's music stream as one voice.
            partVars["music"] = "music";
            EmitPartVariable("music", TopLevelMusic(root), root);
        }

        EmitScore(render, parts, partVars);
        return _sb.ToString();
    }

    /// <summary>
    /// Every part this file has music for: the declared parts, then any part a score
    /// NAMES but never declares.
    /// </summary>
    /// <remarks>
    /// ⚠️ A <c>part</c> declaration is not what makes a part — the SCORE is. The collector
    /// takes its voice names from the render items (RenderSpec.GetVoiceNames) and looks each
    /// one up as a <c>PartBlock</c> inside the sections; a part with nothing to declare (no
    /// clef, no instrument) is simply never written down. This walked the declarations only,
    /// so such a file fell to the "no explicit part" branch below and exported the FILE-level
    /// music stream — which holds the key and the meter and no notes at all. That is the same
    /// silent shape as the loose-section hole: a valid <c>.ly</c> with a blank staff, and a
    /// twin sweep reads it as layout divergence (docs/HANDOFF.md §1 gate list ⑶,
    /// <c>test/ossia-beams</c>).
    /// </remarks>
    private static List<string> PartNames(
        List<PartDeclarationSyntax> parts, RenderDeclarationSyntax? render)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in parts)
            if (seen.Add(part.Name.Text))
                names.Add(part.Name.Text);
        if (render == null)
            return names;
        foreach (var item in RenderRows(render))
            foreach (string? name in RowPartNames(item))
                if (name != null && seen.Add(name))
                    names.Add(name);
        return names;
    }

    /// <summary>
    /// The parts a render row puts MUSIC on: a group's every staff, a staff/tab/ossia's own.
    /// </summary>
    /// <remarks>
    /// ⚠️ A <c>chords</c> / <c>lyrics</c> row names a part too, and it is deliberately NOT
    /// here: its body is a chord or lyric block, not a music stream, so a music variable for
    /// it would be empty and the score would grow a <c>\new Staff</c> for a row this
    /// transpiler cannot write at all (test/lead-sheet has nothing else, and would export a
    /// staff of its chord part). EmitScore reports those rows instead.
    /// </remarks>
    private static IEnumerable<string?> RowPartNames(SyntaxNode item) => item switch
    {
        GrandStaffRenderSyntax group => group.Staves.Select(RenderPartName),
        OssiaRenderSyntax ossia => new[] { OssiaPartName(ossia) },
        StaffRenderSyntax or TabRenderSyntax => new[] { RenderPartName(item) },
        _ => Enumerable.Empty<string?>(),
    };

    /// <summary>
    /// The score's render items, in source order — the same walk
    /// <see cref="Svg.Collector.RenderSpecParser.Parse"/> makes.
    /// </summary>
    /// <remarks>
    /// ⚠️ DESCENDANTS, not direct children. A <c>grandStaff { staff a staff b }</c> holds its
    /// staves one level down, so scanning the score's own children found NO staff in such a
    /// book and the fallback emitted a single staff for the first part — a twin missing a
    /// whole staff, which the sweep then read as layout divergence rather than as a different
    /// score (docs/HANDOFF.md §1 gate list ⑵). A staff INSIDE a group is emitted by that
    /// group, so it drops out here exactly as RenderSpecParser's
    /// <c>IsInsideGrandStaff</c> drops it.
    /// </remarks>
    private static IEnumerable<SyntaxNode> RenderRows(RenderDeclarationSyntax render)
    {
        foreach (var child in render.DescendantNodes())
        {
            switch (child)
            {
                case GrandStaffRenderSyntax:
                case TabRenderSyntax:
                case OssiaRenderSyntax:
                case ChordRowRenderSyntax:
                case LyricsRowRenderSyntax:
                    yield return child;
                    break;
                case StaffRenderSyntax staff when !IsInsideGrandStaff(staff):
                    yield return staff;
                    break;
            }
        }
    }

    private static bool IsInsideGrandStaff(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is GrandStaffRenderSyntax)
                return true;
        return false;
    }

    // ---- Phrases -----------------------------------------------------------

    /// <summary>
    /// Indexes every phrase / variable body in the file so a bare reference inside a
    /// section can be expanded where it stands.
    /// </summary>
    /// <remarks>
    /// The same two declaration shapes MusicXmlExporter indexes (see its
    /// <c>PhraseDeclarationSyntax</c> / <c>VariableDeclarationSyntax</c> cases): this is the
    /// THIRD reader of that pair, and it exists because it was the one missing — a section
    /// body written the ordinary way (<c>melody { partA partB }</c>) exported as an EMPTY
    /// staff, with only a "VariableReference not exported" warning to show for it.
    /// </remarks>
    private void CollectPhrases(CompilationUnitSyntax root)
    {
        foreach (var node in root.DescendantNodes<SyntaxNode>())
        {
            switch (node)
            {
                case PhraseDeclarationSyntax phrase:
                    _phrases[phrase.Name.Text] = phrase.Body;
                    break;
                case VariableDeclarationSyntax varDecl:
                    _phrases[varDecl.Name.Text] = varDecl.Expression;
                    break;
            }
        }
    }

    /// <summary>
    /// Expands a bare phrase reference in place, in a FRESH octave frame.
    /// </summary>
    /// <remarks>
    /// Lily# evaluates every phrase body in the default frame, so the same phrase means the
    /// same pitches at every call site (the collector's <c>RelativeResetMarker</c>); the
    /// reference's own marks (<c>Chorus'</c> / <c>Chorus,</c>) shift that frame. In
    /// LilyPond the same thing is a NESTED <c>\relative</c>, whose reference pitch is
    /// absolute — so the body lands on the pitches Lily# gives it whatever precedes the
    /// reference.
    /// <para>
    /// ⚠️ WHAT THIS CANNOT RENDER, and warns about rather than emitting quietly. LilyPond's
    /// nested <c>\relative</c> is TRANSPARENT to the enclosing frame —
    /// LILYPOND-REF: lily/relative-octave-music.cc:39-45 Relative_octave_music::relative_callback,
    /// which hands the incoming pitch straight
    /// back — whereas Lily# hands off the phrase's ANCHOR (its first note's bare letter,
    /// MeasureCollector.Form.cs). The two agree on the body and disagree only on what a
    /// note AFTER the reference is relative to, so a body that is all references (how the
    /// corpus is written) transpiles exactly, and a mixed one gets a warning naming the
    /// spot. The interval argument (<c>Melody'(3)</c>) and a movable phrase's
    /// auto-transpose are likewise reported, not guessed: both would need the pitches
    /// re-derived, and this exporter is a transpiler that copies pitch tokens verbatim.
    /// </para>
    /// </remarks>
    private string EmitPhraseReference(VariableReferenceSyntax v)
    {
        string name = v.Name.Text;
        if (!_phrases.TryGetValue(name, out var body))
        {
            _warnings.Add($"phrase '{name}' is referenced but not declared — nothing exported for it");
            return "";
        }
        if (!_activePhrases.Add(name))
        {
            _warnings.Add($"phrase '{name}' refers to itself — the inner reference is not expanded");
            return "";
        }
        try
        {
            if (v.DiatonicShiftSteps != 0)
                _warnings.Add(
                    $"phrase reference '{name}' carries an interval argument, which needs the "
                    + "pitches re-derived — the body is exported UNSHIFTED");

            var buf = new LilyPondExporter
            {
                _octaveAbsolute = _octaveAbsolute,
                _anchorOctave = _anchorOctave,
                _phrases = _phrases,
                _activePhrases = _activePhrases,
            };
            buf.EmitMusicStream(MusicItems(body).ToList(), "");
            _warnings.AddRange(buf._warnings);
            string inner = buf._sb.ToString().Replace("\n", " ").Trim();
            if (inner.Length == 0)
                return "";

            // In absolute mode there is no frame to reset: the body's own octave marks are
            // already absolute, so the reference is pure inlining.
            if (_octaveAbsolute)
            {
                if (v.OctaveOffset != 0)
                    _warnings.Add(
                        $"phrase reference '{name}' shifts the octave frame, which an "
                        + "absolute-octave file has none of — the body is exported UNSHIFTED");
                return inner;
            }

            return $"\\relative {ReferencePitch(v.OctaveOffset)} {{ {inner} }}";
        }
        finally
        {
            _activePhrases.Remove(name);
        }
    }

    /// <summary>
    /// The nested block's reference pitch: the PART's anchor octave written LilyPond's way,
    /// moved by the reference's trailing marks.
    /// </summary>
    /// <remarks>
    /// LilyPond writes octave 4 as <c>c'</c>, so the mark count is the anchor minus 3 — a
    /// treble part's <c>c'</c>, a bass part's bare <c>c</c>. See <see cref="_anchorOctave"/>.
    /// </remarks>
    private string ReferencePitch(int octaveOffset) => AnchorPitch(_anchorOctave + octaveOffset);

    /// <summary>An octave number as a LilyPond pitch: 4 → <c>c'</c>, 3 → <c>c</c>.</summary>
    private static string AnchorPitch(int octave)
    {
        int marks = octave - 3;
        return marks >= 0 ? "c" + new string('\'', marks) : "c" + new string(',', -marks);
    }

    /// <summary>
    /// The octave a part's relative pitches are anchored to, resolved the way the layout
    /// resolves it.
    /// </summary>
    /// <remarks>
    /// The same precedence MeasureCollector applies (an explicit <c>octave N</c> property
    /// beats the clef's default, MeasureCollector.cs GetPartDefaults →
    /// <c>partOctave ?? InstrumentDefaults.GetDefaultOctave(ParseClefType(clef))</c>), read
    /// off the same two part properties.
    /// <para>
    /// ⚠️ <c>instrument</c> is NOT read, which is the gate this exporter already declares
    /// and not a new one. An <c>instrument</c> preset is a BUNDLE:
    /// <c>instrument bass</c> means bass clef AND octave 3 AND a sounding pitch an octave
    /// down (InstrumentDefaults.GetTransposition — an electric bass is written an octave
    /// above where it sounds, and that −12 is deliberate). Reading the octave third of that
    /// and not the other two would move the twin's written pitch while leaving its sounding
    /// pitch wrong, i.e. make it wrong in a way that LOOKS right — the failure mode this
    /// file's "transpiler, not a re-derivation" rule exists to avoid. A part whose octave
    /// comes only from an instrument preset therefore still exports at the treble anchor,
    /// exactly as before, and its twin is already known not to be a pair
    /// (HANDOFF: test/tab-as-numbers).
    /// </para>
    /// </remarks>
    private static int AnchorOctaveOf(PartDeclarationSyntax part)
    {
        string? octave = PartProperty(part, "octave");
        if (octave != null && int.TryParse(octave, out int explicitOctave))
            return explicitOctave;
        string? clef = PartProperty(part, "clef");
        return InstrumentDefaults.GetDefaultOctave(
            clef != null
                ? MeasureCollector.ParseClefType(clef.ToLowerInvariant())
                : ClefType.Treble);
    }

    // ---- Header ------------------------------------------------------------

    private void EmitHeader(CompilationUnitSyntax root)
    {
        _sb.Append("\\version \"").Append(LilyPondVersion).Append("\"\n\n");

        var meta = root.DescendantNodes<MetadataDeclarationSyntax>().ToList();
        string? title = MetaString(meta, "title");
        string? composer = MetaString(meta, "composer");
        if (title != null || composer != null)
        {
            _sb.Append("\\header {\n");
            if (title != null) _sb.Append("  title = \"").Append(Escape(title)).Append("\"\n");
            if (composer != null) _sb.Append("  composer = \"").Append(Escape(composer)).Append("\"\n");
            _sb.Append("}\n\n");
        }
    }

    private static string? MetaString(List<MetadataDeclarationSyntax> meta, string keyword)
    {
        foreach (var m in meta)
            if (m.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                return m.StringValue;
        return null;
    }

    // ---- Part music variable ----------------------------------------------

    private void EmitPartVariable(string varName, List<SyntaxNode> music, CompilationUnitSyntax root)
    {
        // ⚠️ The two modes anchor DIFFERENTLY on purpose. Absolute octave is middle C
        //   whatever the clef (OctaveContext: "clef default is deliberately NOT used here"),
        //   so \fixed is always c'; relative follows the part's own default octave.
        string wrapper = _octaveAbsolute
            ? "\\fixed c'"
            : "\\relative " + AnchorPitch(_anchorOctave);
        _sb.Append(varName).Append(" = ").Append(wrapper).Append(" {\n");

        // Score-level settings (tempo/key/time live at file scope in Lily#).
        EmitScoreSettings(root);

        EmitMusicStream(music, indent: "  ");
        _sb.Append("}\n\n");
    }

    private void EmitScoreSettings(CompilationUnitSyntax root)
    {
        // Only the file-level (top-level) settings, in source order.
        foreach (var m in root.Members)
        {
            switch (m)
            {
                case TempoDeclarationSyntax t: _sb.Append("  ").Append(EmitTempo(t)).Append('\n'); break;
                case KeySignatureSyntax k: _sb.Append("  ").Append(EmitKey(k)).Append('\n'); break;
                case TimeSignatureSyntax ts: _sb.Append("  ").Append(EmitTime(ts)).Append('\n'); break;
                case PartialDeclarationSyntax p: _sb.Append("  ").Append(EmitPartial(p)).Append('\n'); break;
            }
        }
    }

    // ---- Ordered music (flatten sections by form) --------------------------

    // Concatenate the part's sections into one item stream, in the order the
    // primary form references them. A |: … :| repeat can span several sections,
    // so grouping must happen AFTER this flattening (in EmitMusicStream).
    private List<SyntaxNode> OrderedMusic(
        string partName, PartDeclarationSyntax? part, FormDeclarationSyntax? form,
        List<SectionDeclarationSyntax> allSections)
    {
        // A section reaches this part in one of TWO spellings, and both must be read.
        //   part-major:    part m { section A { c8 d } }   — music inline in the section
        //   section-major: section A { m { c8 d } }        — the section sits OUTSIDE the
        //                                                    part and names it with a PartBlock
        // ⚠️ Only the first was read here. `allSections` was collected by the caller FOR the
        // second and then never used, so every file written the ordinary way exported an
        // EMPTY part variable — a valid .ly that renders a blank staff, silently. All ten
        // showcase fixtures and most of test/ are section-major.
        // (MusicXmlExporter.EmitPartMajorSection carries the mirror-image note: that exporter
        // was missing the OTHER spelling and had the same symptom.)
        // A part a score names but never declares (`ossia melody` with no `part melody`)
        // has no block of its own to hold sections — only the second and third spellings
        // can reach it.
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
                if (s.Parent is CompilationUnitSyntax && LooseSectionMusic(s).Any())
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
            foreach (var name in FormSectionOrder(form))
                if (byName.TryGetValue(name, out var entry))
                {
                    result.AddRange(SectionHeaderMusic(entry.Section));
                    result.AddRange(ContainerMusic(entry.Container));
                }
        }
        else
        {
            foreach (var entry in inOrder)
            {
                result.AddRange(SectionHeaderMusic(entry.Section));
                result.AddRange(ContainerMusic(entry.Container));
            }
        }

        // Also carry any part-level clef declared outside a section (e.g. a
        // mid-part clef change is inside a section and handled there).
        return result;
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
        if (FirstDirect<TimeSignatureSyntax>(section) is { } time) yield return time;
        if (FirstDirect<TempoDeclarationSyntax>(section) is { } tempo) yield return tempo;
        if (FirstDirect<KeySignatureSyntax>(section) is { } key) yield return key;
        if (FirstDirect<PartialDeclarationSyntax>(section) is { } partial) yield return partial;
    }

    /// <summary>The first direct-child directive of type <typeparamref name="T"/>.</summary>
    private static T? FirstDirect<T>(SectionDeclarationSyntax section) where T : SyntaxNode
    {
        foreach (var child in EnumerateChildren(section))
            if (child is T t)
                return t;
        return null;
    }

    /// <summary>
    /// True when the section has a direct-child MUSIC node, as opposed to only directives and
    /// part / chord / lyric blocks — MeasureCollector.Form.cs SectionHasInlineMusic.
    /// </summary>
    private static bool SectionHasInlineMusic(SectionDeclarationSyntax section)
    {
        foreach (var child in EnumerateChildren(section))
        {
            // ⚠️ The keyword and the braces are children too. Dropping this line made every
            // section look like it had inline music, so the header was never emitted.
            if (child is SyntaxTokenNode)
                continue;
            if (child is PartBlockSyntax or ChordPartBlockSyntax or LyricsBlockSyntax)
                continue;
            if (child is KeySignatureSyntax or TimeSignatureSyntax or TempoDeclarationSyntax
                or PartialDeclarationSyntax or ClefDeclarationSyntax or OctaveDirectiveSyntax
                or OverrideDeclarationSyntax or RevertDeclarationSyntax or OnceModifierSyntax)
                continue;
            return true;
        }
        return false;
    }

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

    private static IEnumerable<string> FormSectionOrder(FormDeclarationSyntax form)
    {
        foreach (var child in EnumerateChildren(form))
        {
            switch (child)
            {
                case SectionReferenceSyntax r: yield return r.SectionName; break;
                case FormAlternativeSyntax a: yield return a.SectionName.Text; break;
                default:
                    // `~Section` (silent reference) has no red node — it is a generic
                    // node whose slot 1 is the section-name token.
                    if (child.Kind == SyntaxKind.SilentSectionReference
                        && child.GetChild(1) is SyntaxTokenNode name)
                        yield return name.Text;
                    break;
            }
        }
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

    private void EmitMusicStream(List<SyntaxNode> items, string indent)
    {
        var line = new StringBuilder(indent);
        int i = 0;
        while (i < items.Count)
        {
            var item = items[i];

            // Inline |: … :| repeat span → \repeat volta N { … } \alternative { … }
            if (item is BarlineSyntax { BarToken.Kind: SyntaxKind.RepeatStartBar or SyntaxKind.RepeatBothBar })
            {
                FlushLine(line, indent);
                i = EmitInlineRepeat(items, i, indent);
                continue;
            }

            // The one regime where a phrase reference does not transpile exactly: LilyPond's
            // nested \relative hands the enclosing frame back UNCHANGED
            // (lily/relative-octave-music.cc:39-45 relative_callback), while Lily# hands off
            // the phrase's ANCHOR. Nothing after the reference sees the difference unless
            // something after it is a pitch, so the warning is raised there and only there.
            if (item is VariableReferenceSyntax vref && FollowedByPitch(items, i))
                _warnings.Add(
                    $"a note follows the phrase reference '{vref.Name.Text}': LilyPond makes it "
                    + "relative to the pitch BEFORE the reference, Lily# to the phrase's anchor "
                    + "— check that stretch by hand");

            string tok = EmitItem(item);
            if (tok.Length == 0) { i++; continue; }

            AppendToken(line, tok, indent);

            // A barline or a break ends the current visual line.
            if (item is BarlineSyntax || item is BreakSyntax)
                FlushLine(line, indent);

            i++;
        }
        FlushLine(line, indent);
    }

    /// <summary>
    /// Whether anything after <paramref name="index"/> in this stream carries a pitch —
    /// i.e. whether the enclosing octave frame is read again after this point.
    /// </summary>
    private static bool FollowedByPitch(List<SyntaxNode> items, int index)
    {
        for (int j = index + 1; j < items.Count; j++)
            if (items[j] is NoteSyntax or ChordSyntax)
                return true;
        return false;
    }

    // items[start] is |: . Returns the index just past the matching :| (and any
    // trailing [n. …] alternatives).
    private int EmitInlineRepeat(List<SyntaxNode> items, int start, string indent)
    {
        // Collect common items until the first alternative or the closing :|,
        // tracking nesting so a nested |: … :| does not close us early.
        var common = new List<SyntaxNode>();
        var alternatives = new List<InlineVoltaSyntax>();
        int depth = 0;
        int repeatCount = 2;
        int i = start + 1;
        bool closed = false;

        for (; i < items.Count; i++)
        {
            var it = items[i];
            if (it is BarlineSyntax { BarToken.Kind: SyntaxKind.RepeatStartBar } && alternatives.Count == 0)
            {
                depth++; common.Add(it); continue;
            }
            if (it is BarlineSyntax rb && rb.BarToken.Kind is SyntaxKind.RepeatEndBar or SyntaxKind.RepeatBothBar)
            {
                if (depth > 0) { depth--; common.Add(it); continue; }
                repeatCount = rb.HasExplicitRepeatCount ? rb.RepeatCount : repeatCount;
                closed = true;
                // Endings can follow the :| ([2. …]); keep scanning for them.
                continue;
            }
            if (it is InlineVoltaSyntax v)
            {
                alternatives.Add(v);
                continue;
            }
            if (closed)
            {
                // Past :| with no more endings — the repeat is done.
                if (it is BreakSyntax) { /* absorb a break right after :| */ continue; }
                break;
            }
            common.Add(it);
        }

        if (alternatives.Count > 0)
        {
            int maxVolta = alternatives.SelectMany(a => a.Numbers).DefaultIfEmpty(2).Max();
            repeatCount = Math.Max(repeatCount, maxVolta);
        }

        _sb.Append(indent).Append("\\repeat volta ").Append(repeatCount).Append(" {\n");
        EmitMusicStream(common, indent + "  ");
        _sb.Append(indent).Append("}\n");

        if (alternatives.Count > 0)
        {
            _sb.Append(indent).Append("\\alternative {\n");
            foreach (var alt in alternatives)
            {
                _sb.Append(indent).Append("  {\n");
                EmitMusicStream(alt.Items.ToList(), indent + "    ");
                _sb.Append(indent).Append("  }\n");
            }
            _sb.Append(indent).Append("}\n");
        }

        return i;
    }

    // ---- Per-item emit -----------------------------------------------------

    private string EmitItem(SyntaxNode item) => item switch
    {
        NoteSyntax n => EmitNote(n),
        RestSyntax r => EmitRest(r),
        ChordSyntax c => EmitChord(c),
        BarlineSyntax b => EmitBarline(b),
        BreakSyntax br => br.IsNoBreak ? "\\noBreak" : "\\break",
        TieSyntax => "~",
        SlurSyntax s => s.IsOpen ? "(" : ")",
        BeamMarkerSyntax bm => bm.IsStart ? "[" : "]",
        DynamicSyntax d => "\\" + d.DynamicToken.Text,
        KeySignatureSyntax k => EmitKey(k),
        TimeSignatureSyntax ts => EmitTime(ts),
        TempoDeclarationSyntax t => EmitTempo(t),
        ClefDeclarationSyntax cl => "\\clef " + cl.ClefName.Text,
        PartialDeclarationSyntax p => EmitPartial(p),
        TupletExpressionSyntax tup => EmitTuplet(tup),
        ParallelExpressionSyntax par => EmitParallel(par),
        GraceExpressionSyntax g => EmitGrace(g),
        RepeatExpressionSyntax rep => EmitRepeat(rep),
        MusicMarkSyntax mk => EmitMark(mk),
        NavigationMarkSyntax nav => EmitNavMark(nav),
        StringNumberAnnotationSyntax sn => sn.StringNumberToken.Text,
        ArticulationSyntax a => MapArticulation(a),
        VariableReferenceSyntax vr => EmitPhraseReference(vr),
        // Structural nodes that carry no inline music are skipped silently.
        OctaveDirectiveSyntax or MetadataDeclarationSyntax
            or SectionDeclarationSyntax or FormDeclarationSyntax
            or PartDeclarationSyntax or RenderDeclarationSyntax => "",
        _ => Skip(item),
    };

    private string EmitNote(NoteSyntax n)
    {
        var (prefix, suffix) = SplitAttachments(n.Articulations);
        string trem = n.Tremolo is { } t ? t.Text : "";
        return prefix + EmitPitch(n.Pitch) + EmitDuration(n.Duration) + trem + suffix;
    }

    private string EmitRest(RestSyntax r)
    {
        var (prefix, suffix) = SplitAttachments(r.Articulations);
        string mmr = r.IsMultiMeasure ? "*" + r.MeasureCount : "";
        return prefix + r.RestToken.Text + EmitDuration(r.Duration) + mmr + suffix;
    }

    private string EmitChord(ChordSyntax c)
    {
        var sb = new StringBuilder("<");
        bool first = true;
        foreach (var p in c.Pitches)
        {
            if (!first) sb.Append(' ');
            sb.Append(EmitPitch(p));
            first = false;
        }
        sb.Append('>');
        int off = c.ChordOctaveOffset;
        if (off > 0) sb.Append(new string('\'', off));
        else if (off < 0) sb.Append(new string(',', -off));
        sb.Append(EmitDuration(c.Duration));
        var (prefix, suffix) = SplitAttachments(c.Articulations);
        return prefix + sb.ToString() + suffix;
    }

    // A note's attachments split into those that must precede the note (a
    // rehearsal \mark, a \deadNote prefix) and those that trail it (string
    // numbers, ties, dynamics, articulation scripts).
    private (string Prefix, string Suffix) SplitAttachments(IEnumerable<SyntaxNode> arts)
    {
        var prefix = new StringBuilder();
        var suffix = new StringBuilder();
        foreach (var a in arts)
        {
            switch (a)
            {
                case MusicMarkSyntax mk:
                    string m = EmitMark(mk);
                    if (m.Length > 0) prefix.Append(m).Append(' ');
                    break;
                case ArticulationSyntax art when IsDeadNote(art):
                    prefix.Append("\\deadNote ");
                    break;
                default:
                    suffix.Append(EmitAttachment(a));
                    break;
            }
        }
        return (prefix.ToString(), suffix.ToString());
    }

    private static bool IsDeadNote(ArticulationSyntax a)
        => a.NameToken.Text.Equals("dead", StringComparison.OrdinalIgnoreCase);

    // `@mark("Intro")` → a rehearsal mark. MarkName joins the tokens with '.',
    // so the label is what follows "mark." with the quotes stripped.
    private string EmitMark(MusicMarkSyntax mk)
    {
        string name = mk.MarkName;
        if (name.StartsWith("mark.", StringComparison.OrdinalIgnoreCase))
        {
            string label = name.Substring("mark.".Length).Trim('"');
            return $"\\mark \\markup {{ \\box {label} }}";
        }
        _warnings.Add($"@{name} dropped (out of scope)");
        return "";
    }

    private string EmitAttachment(SyntaxNode a) => a switch
    {
        StringNumberAnnotationSyntax sn => sn.StringNumberToken.Text, // "\4" — LilyPond-valid
        TieSyntax => "~",
        SlurSyntax s => s.IsOpen ? "(" : ")",
        BeamMarkerSyntax bm => bm.IsStart ? "[" : "]",
        DynamicSyntax d => "\\" + d.DynamicToken.Text,
        ArticulationSyntax art => MapArticulation(art),
        MusicMarkSyntax mk => EmitMark(mk),
        _ => "",
    };

    private static string EmitPitch(PitchSyntax p)
    {
        var sb = new StringBuilder(p.PitchToken.Text);
        int off = p.OctaveOffset;
        if (off > 0) sb.Append(new string('\'', off));
        else if (off < 0) sb.Append(new string(',', -off));
        return sb.ToString();
    }

    private static string EmitDuration(DurationSyntax? d)
        => d == null ? "" : d.NumberToken.Text + new string('.', d.DotCount);

    private string EmitBarline(BarlineSyntax b) => b.BarToken.Kind switch
    {
        SyntaxKind.Bar => "|",
        SyntaxKind.DoubleBar => "\\bar \"||\"",
        SyntaxKind.FinalBar => "\\bar \"|.\"",
        SyntaxKind.DashedBar => "\\bar \"!\"",
        // Repeat barlines are consumed by EmitInlineRepeat; a stray one is a
        // best-effort fallback.
        SyntaxKind.RepeatStartBar => "\\bar \".|:\"",
        SyntaxKind.RepeatEndBar => "\\bar \":|.\"",
        SyntaxKind.RepeatBothBar => "\\bar \":|.|:\"",
        _ => "|",
    };

    private string EmitKey(KeySignatureSyntax k)
    {
        if (k.IsCustom) { _warnings.Add("custom key signature emitted as \\key c \\major (unsupported)"); return "\\key c \\major"; }
        string mode = k.IsMajor ? "major" : k.Mode.Text.ToLowerInvariant();
        return "\\key " + EmitPitch(k.Pitch) + " \\" + mode;
    }

    private static string EmitTime(TimeSignatureSyntax ts)
        => ts.IsSenzaMisura
            ? "\\cadenzaOn"
            : "\\time " + (ts.BeatsText ?? ts.Beats.ToString()) + "/" + ts.BeatType;

    private static string EmitTempo(TempoDeclarationSyntax t)
    {
        if (t.Bpm is int bpm)
        {
            int unit = t.BeatUnit is int u ? u : 4;
            string dots = new string('.', t.BeatDots);
            return $"\\tempo {unit}{dots} = {bpm}";
        }
        if (!string.IsNullOrEmpty(t.Marking))
            return "\\tempo \"" + Escape(t.Marking!) + "\"";
        return "";
    }

    private static string EmitPartial(PartialDeclarationSyntax p)
    {
        var d = p.Duration;
        return d == null ? "" : "\\partial " + d.NumberToken.Text + new string('.', d.DotCount);
    }

    private string EmitTuplet(TupletExpressionSyntax tup)
    {
        var inner = new StringBuilder();
        var saved = _sb.Length;
        // Reuse EmitMusicStream via a temporary buffer.
        var buf = new LilyPondExporter
        { _octaveAbsolute = _octaveAbsolute, _anchorOctave = _anchorOctave };
        buf.EmitMusicStream(MusicItems(tup.Body).ToList(), "");
        _warnings.AddRange(buf._warnings);
        string body = buf._sb.ToString().Replace("\n", " ").Trim();
        return $"\\tuplet {tup.Numerator.Text}/{tup.Denominator.Text} {{ {body} }}";
    }

    /// <summary>
    /// A <c>voice { … } voice { … }</c> run as LilyPond's simultaneous-voice shorthand.
    /// </summary>
    /// <remarks>
    /// LilyPond's <c>&lt;&lt; { … } \\ { … } &gt;&gt;</c> is not merely "these play together":
    /// the <c>\\</c> separator creates a Voice per branch AND applies \voiceOne, \voiceTwo, …
    /// to them (ly/engraver-init.ly), which is where the forced stem directions come from.
    /// That is the same rule Lily# bakes into the model — MeasureCollector's
    /// ResolveVoiceStemDirections, via VoiceDefaults.GetDefaultStemUp — so the two sides agree
    /// by construction.
    /// <para>
    /// ⚠️ A LONE voice block emits its contents bare, with no wrapper. Both engines leave a
    /// single voice's stems to the pitch rule (ResolveVoiceStemDirections returns early at
    /// <c>voices.Length &lt;= 1</c>; LilyPond applies no voice settings without a <c>\\</c>),
    /// and wrapping it would not change that on either side — but the bare form says so.
    /// </para>
    /// <para>
    /// ⚠️ Before this existed the whole run fell to <see cref="Skip"/> ("ParallelExpression not
    /// exported", 29 of them across the corpus) and every polyphonic book exported as an EMPTY
    /// staff — 11 twins, which the twin sweep then read as layout divergence. That is the
    /// FOURTH hole of this shape; the other three were VariableReference, phrase references and
    /// the relative-octave anchor. docs/HANDOFF.md §1 gate list.
    /// </para>
    /// </remarks>
    private string EmitParallel(ParallelExpressionSyntax par)
    {
        var bodies = new List<string>();
        foreach (var (_, block) in par.NamedVoices)
        {
            var buf = new LilyPondExporter
            {
                _octaveAbsolute = _octaveAbsolute,
                _anchorOctave = _anchorOctave,
                _phrases = _phrases,
                _activePhrases = _activePhrases,
            };
            buf.EmitMusicStream(MusicItems(block).ToList(), "");
            _warnings.AddRange(buf._warnings);
            string body = buf._sb.ToString().Replace("\n", " ").Trim();
            if (body.Length > 0)
                bodies.Add(body);
        }

        if (bodies.Count == 0)
            return "";
        if (bodies.Count == 1)
            return bodies[0];
        return "<< { " + string.Join(" } \\\\ { ", bodies) + " } >>";
    }

    private string EmitGrace(GraceExpressionSyntax g)
    {
        string kw = g.IsAcciaccatura ? "\\acciaccatura" : g.IsAppoggiatura ? "\\appoggiatura" : "\\grace";
        var buf = new LilyPondExporter
        { _octaveAbsolute = _octaveAbsolute, _anchorOctave = _anchorOctave };
        buf.EmitMusicStream(MusicItems(g.Body).ToList(), "");
        _warnings.AddRange(buf._warnings);
        string body = buf._sb.ToString().Replace("\n", " ").Trim();
        return $"{kw} {{ {body} }}";
    }

    private string EmitRepeat(RepeatExpressionSyntax rep)
    {
        string type = rep.RepeatType.Text;
        string count = rep.Count.Text;
        var buf = new LilyPondExporter
        { _octaveAbsolute = _octaveAbsolute, _anchorOctave = _anchorOctave };
        buf.EmitMusicStream(MusicItems(rep.Body).ToList(), "");
        _warnings.AddRange(buf._warnings);
        string body = buf._sb.ToString().Replace("\n", " ").Trim();
        return $"\\repeat {type} {count} {{ {body} }}";
    }

    private string MapArticulation(ArticulationSyntax a)
    {
        // Name-based marks whose Type is None (resolved downstream in Lily#).
        switch (a.NameToken.Text.ToLowerInvariant())
        {
            case "fall": return "\\bendAfter #-4"; // a fall/drop off the note
            case "dead": return "\\deadNote";      // normally intercepted as a prefix
        }

        // Common LilyPond articulations. `@name.up/.down` → -^ / _^ direction.
        string glyph = a.Type switch
        {
            ArticulationType.Staccato => "\\staccato",
            ArticulationType.Staccatissimo => "\\staccatissimo",
            ArticulationType.Accent => "\\accent",
            ArticulationType.Marcato => "\\marcato",
            ArticulationType.Tenuto => "\\tenuto",
            ArticulationType.Fermata => "\\fermata",
            ArticulationType.Trill => "\\trill",
            ArticulationType.Mordent => "\\mordent",
            ArticulationType.Turn => "\\turn",
            ArticulationType.Prall => "\\prall",
            _ => "",
        };
        if (glyph.Length == 0)
        {
            _warnings.Add($"articulation @{a.NameToken.Text} not mapped, dropped");
            return "";
        }
        string dir = a.ForcedAbove switch { true => "^", false => "_", null => "-" };
        return dir + glyph;
    }

    // Navigation marks (segno/coda/fine/D.C./D.S. …) as standalone \mark commands.
    private static string EmitNavMark(NavigationMarkSyntax nav) => nav.MarkType switch
    {
        NavigationMarkType.Segno => "\\mark \\markup { \\musicglyph #\"scripts.segno\" }",
        NavigationMarkType.Coda => "\\mark \\markup { \\musicglyph #\"scripts.coda\" }",
        NavigationMarkType.Fine => "\\mark \\markup { \\italic \"Fine\" }",
        NavigationMarkType.ToCoda => "\\mark \\markup { \\italic \"To Coda\" }",
        NavigationMarkType.DaCapo => "\\mark \\markup { \\italic \"D.C.\" }",
        NavigationMarkType.DaCapoAlFine => "\\mark \\markup { \\italic \"D.C. al Fine\" }",
        NavigationMarkType.DaCapoAlCoda => "\\mark \\markup { \\italic \"D.C. al Coda\" }",
        NavigationMarkType.DalSegno => "\\mark \\markup { \\italic \"D.S.\" }",
        NavigationMarkType.DalSegnoAlFine => "\\mark \\markup { \\italic \"D.S. al Fine\" }",
        NavigationMarkType.DalSegnoAlCoda => "\\mark \\markup { \\italic \"D.S. al Coda\" }",
        _ => "",
    };

    private string Skip(SyntaxNode item)
    {
        _warnings.Add($"{item.Kind} not exported");
        return "";
    }

    // ---- Score / staff / tab ----------------------------------------------

    private void EmitScore(RenderDeclarationSyntax? render, List<PartDeclarationSyntax> parts,
        Dictionary<string, string> partVars)
    {
        _sb.Append("\\score {\n");

        // The rows of the system, in source order. An ossia is a row like any other and
        // is MOVED into place by alignAboveContext, exactly as RenderSpec.OrderedItems
        // moves it — see EmitOssia.
        var rows = new List<string>();
        string? lastMainStaffPart = null;   // what an ossia written next would sit above
        var alignedAbove = new HashSet<string>(StringComparer.Ordinal);
        if (render != null)
        {
            foreach (var item in RenderRows(render))
            {
                switch (item)
                {
                    case GrandStaffRenderSyntax group:
                        rows.Add(EmitStaffGroup(group, parts, partVars));
                        // LilyPond aligns above a STAFF, so a group is named by its first
                        // staff — the row Lily# would insert the ossia in front of.
                        lastMainStaffPart = group.Staves
                            .Select(RenderPartName).FirstOrDefault(n => n != null)
                            ?? lastMainStaffPart;
                        break;
                    case StaffRenderSyntax st:
                        rows.Add(EmitStaff(RenderPartName(st), parts, partVars, tab: false, "    "));
                        lastMainStaffPart = RenderPartName(st) ?? lastMainStaffPart;
                        break;
                    case TabRenderSyntax tb:
                        rows.Add(EmitStaff(RenderPartName(tb), parts, partVars, tab: true, "    "));
                        lastMainStaffPart = RenderPartName(tb) ?? lastMainStaffPart;
                        break;
                    case OssiaRenderSyntax os:
                        rows.Add(EmitOssia(os, parts, partVars, lastMainStaffPart));
                        if (lastMainStaffPart != null)
                            alignedAbove.Add(lastMainStaffPart);
                        break;
                    // A chord / lyrics row needs a music stream this transpiler has no reader
                    // for (chord and lyric blocks are collected separately), so it is REPORTED
                    // rather than dropped: a twin silently missing a row is the shape that has
                    // cost this exporter five holes already.
                    case ChordRowRenderSyntax chords:
                        _warnings.Add($"chord row '{chords.PartName}' is not exported — the twin has no chord row");
                        break;
                    case LyricsRowRenderSyntax lyrics:
                        _warnings.Add($"lyrics row '{lyrics.PartName}' is not exported — the twin has no lyrics row");
                        break;
                }
            }
        }
        if (rows.Count == 0 && partVars.Count > 0)
        {
            // Fall back to a plain staff for the first part.
            var first = partVars.First();
            rows.Add(EmitStaff(first.Key, parts, partVars, tab: false, "    "));
        }

        // An ossia's alignAboveContext names a context, so the staff it decorates has to
        // carry that id. Only the staves an ossia actually names get one.
        foreach (string partName in alignedAbove)
            for (int i = 0; i < rows.Count; i++)
                rows[i] = NameStaffContext(rows[i], partName, partVars);

        if (rows.Count == 1)
        {
            _sb.Append(rows[0]);
        }
        else
        {
            // ⚠️ Plain simultaneity, NOT \new StaffGroup. Loose `staff a staff b` rows are
            // separate single-staff groups in Lily# (RenderSpec.ToStaffGroups →
            // StaffGroup.CreateSingle each), so a StaffGroup context would add a bracket and
            // span bars the .lys never asked for. A DECLARED group emits its own context.
            _sb.Append("  <<\n");
            foreach (var s in rows) _sb.Append(s);
            _sb.Append("  >>\n");
        }
        _sb.Append("  \\layout {}\n}\n");
    }

    /// <summary>
    /// A declared staff group — <c>grandStaff</c> / <c>staffGroup</c> / <c>choirStaff</c> —
    /// as the LilyPond context of the same name.
    /// </summary>
    /// <remarks>
    /// The three map one-to-one, and LilyPond derives them from one another the same way
    /// Lily# does: <c>GrandStaff</c> is <c>StaffGroup</c> with a brace instead of a bracket,
    /// <c>ChoirStaff</c> is <c>StaffGroup</c> minus the span bars
    /// (LILYPOND-REF: ly/engraver-init.ly:468-557 Span_bar_engraver — the StaffGroup
    /// context, then GrandStaff and ChoirStaff derived from it).
    /// <para>
    /// ⚠️ <c>GrandStaff</c>, not <c>PianoStaff</c>: PianoStaff adds
    /// <c>Keep_alive_together_engraver</c>, so its staves are "only removed together, never
    /// separately" (ly/engraver-init.ly:535-544 PianoStaff / Keep_alive_together_engraver)
    /// — and Lily#'s grandStaff removes them
    /// separately, so a PianoStaff twin would not be a pair for any book with
    /// <c>removeEmpty</c>.
    /// </para>
    /// </remarks>
    private string EmitStaffGroup(GrandStaffRenderSyntax group,
        List<PartDeclarationSyntax> parts, Dictionary<string, string> partVars)
    {
        string context = group.GrandStaffKeyword.Kind switch
        {
            SyntaxKind.StaffGroupKeyword => "StaffGroup",
            SyntaxKind.ChoirStaffKeyword => "ChoirStaff",
            _ => "GrandStaff",
        };
        var sb = new StringBuilder();
        sb.Append("    \\new ").Append(context).Append(" <<\n");
        foreach (var staff in group.Staves)
            sb.Append(EmitStaff(RenderPartName(staff), parts, partVars, tab: false, "      "));
        sb.Append("    >>\n");
        return sb.ToString();
    }

    /// <summary>
    /// An <c>ossia</c> row: a small staff with no meter and no opening clef, pulled above
    /// the staff it decorates.
    /// </summary>
    /// <remarks>
    /// Every tweak here is one Lily# already spells in its own renderer, so the twin says the
    /// same thing twice rather than inventing a convention:
    /// <list type="bullet">
    /// <item><c>alignAboveContext</c> — RenderSpec.OrderedItems moves an ossia directly above
    ///   the nearest PRECEDING main row, which is the property LilyPond's own ossia recipe
    ///   uses for it (Documentation/en/notation/staff.itely, NR "Ossia staves").</item>
    /// <item><c>\remove Time_signature_engraver</c> — SharedRenderer prints no meter on an
    ///   ossia at all. LILYPOND-REF: ly/engraver-init.ly Time_signature_engraver, the Staff
    ///   context's engraver that the same recipe removes.</item>
    /// <item><c>firstClef = ##f</c> — SharedRenderer's <c>drawClef</c> is false on the ossia's
    ///   FIRST appearance. LILYPOND-REF: lily/clef-engraver.cc Clef_engraver, which creates
    ///   the opening clef only when a previous clef exists or firstClef is true.</item>
    /// <item><c>fontSize = #-3</c> with <c>StaffSymbol.staff-space</c>/<c>thickness</c> at
    ///   <c>magstep -3</c> — EngravingDefaults.OssiaScale IS magstep(-3) = 0.7071 and cites
    ///   this spelling. LILYPOND-REF: scm/lily-library.scm magstep, 2^(s/6).
    ///   ⚠️ NOT <c>\magnifyStaff #2/3</c>, which the NR example uses: 2/3 is a different
    ///   number (0.667) and the twin would be a size apart.</item>
    /// </list>
    /// ⚠️ These are LP-DERIVED even though none is a literal transcription — §7.6 ⒝, so they
    /// carry LILYPOND-REF and not LILYSHARP-OWN. What could not be copied literally is the
    /// SHAPE: Lily# spells the ossia convention as renderer behaviour and LilyPond as context
    /// properties, so the twin has to restate it in the other vocabulary.
    /// </remarks>
    private string EmitOssia(OssiaRenderSyntax ossia, List<PartDeclarationSyntax> parts,
        Dictionary<string, string> partVars, string? alignAbovePart)
    {
        string? partName = OssiaPartName(ossia);
        string varName = partName != null && partVars.TryGetValue(partName, out var v)
            ? v : partVars.Values.FirstOrDefault() ?? "music";

        var sb = new StringBuilder();
        sb.Append("    \\new Staff \\with {\n");
        sb.Append("      \\remove Time_signature_engraver\n");
        if (alignAbovePart != null && partVars.TryGetValue(alignAbovePart, out var above))
            sb.Append("      alignAboveContext = \"").Append(above).Append("\"\n");
        sb.Append("      fontSize = #-3\n");
        sb.Append("      \\override StaffSymbol.staff-space = #(magstep -3)\n");
        sb.Append("      \\override StaffSymbol.thickness = #(magstep -3)\n");
        sb.Append("      firstClef = ##f\n");
        sb.Append("    } { ");
        // The ossia's own clef word (`ossia bass melody`) when it has one, else the part's.
        // ⚠️ An explicit clef is written even though firstClef suppresses the OPENING one:
        // the glyph stays hidden, but the notes still have to be READ in that clef.
        string? clef = OssiaClef(ossia)
                       ?? (parts.FirstOrDefault(p => p.Name.Text == partName) is { } part
                           ? PartProperty(part, "clef")
                           : null);
        if (clef != null) sb.Append("\\clef ").Append(clef).Append(' ');
        sb.Append('\\').Append(varName).Append(" }\n");
        return sb.ToString();
    }

    /// <summary>The part an ossia row names — its LAST token, the same slot
    /// <see cref="Svg.Collector.RenderSpecParser"/>'s ParseOssia reads.</summary>
    private static string? OssiaPartName(OssiaRenderSyntax ossia)
        => ossia.SlotCount >= 2 && ossia.GetChild(ossia.SlotCount - 1) is SyntaxTokenNode name
            ? name.Text
            : null;

    /// <summary>The clef word of <c>ossia [clef] part</c>, or null when the row is just
    /// <c>ossia part</c> (a lone word is the PART, never a clef).</summary>
    private static string? OssiaClef(OssiaRenderSyntax ossia)
        => ossia.SlotCount >= 3 && ossia.GetChild(1) is SyntaxTokenNode clef ? clef.Text : null;

    /// <summary>
    /// Gives an already-emitted <c>\new Staff</c> row the context id an ossia aligns above.
    /// </summary>
    private static string NameStaffContext(string row, string partName,
        Dictionary<string, string> partVars)
    {
        if (!partVars.TryGetValue(partName, out var varName))
            return row;
        // The row's own variable reference is what identifies it; `\with` rows (the ossias
        // themselves) never match, because the marker is immediately followed by `{`.
        string marker = "\\new Staff { ";
        int at = row.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0 || !row.Contains("\\" + varName + " }", StringComparison.Ordinal))
            return row;
        return row.Insert(at + "\\new Staff".Length, " = \"" + varName + "\"");
    }

    private string EmitStaff(string? partName, List<PartDeclarationSyntax> parts,
        Dictionary<string, string> partVars, bool tab, string indent)
    {
        string varName = partName != null && partVars.TryGetValue(partName, out var v)
            ? v : partVars.Values.FirstOrDefault() ?? "music";
        var part = parts.FirstOrDefault(p => p.Name.Text == partName)
                   ?? (parts.Count == 1 ? parts[0] : null);
        string? clef = part != null ? PartProperty(part, "clef") : null;

        var sb = new StringBuilder();
        if (tab)
        {
            string tuning = TabTuning(part);
            sb.Append(indent).Append("\\new TabStaff");
            if (tuning.Length > 0)
                sb.Append(" \\with { stringTunings = #").Append(tuning).Append(" }");
            sb.Append(" { \\").Append(varName).Append(" }\n");
        }
        else
        {
            sb.Append(indent).Append("\\new Staff { ");
            if (clef != null) sb.Append("\\clef ").Append(clef).Append(' ');
            sb.Append('\\').Append(varName).Append(" }\n");
        }
        return sb.ToString();
    }

    // `tuning bass` on the part → LilyPond predefined tuning name.
    private static string TabTuning(PartDeclarationSyntax? part)
    {
        string? name = part != null ? PartProperty(part, "tuning")?.ToLowerInvariant() : null;
        return name switch
        {
            "bass5" => "bass-five-string-tuning",
            "guitar" => "guitar-tuning",
            _ => "bass-four-string-tuning",
        };
    }

    // The first value token of a part-header property (`clef bass` → "bass").
    private static string? PartProperty(PartDeclarationSyntax part, string name)
    {
        foreach (var prop in part.Properties)
            if (prop.NameToken.Text.Equals(name, StringComparison.OrdinalIgnoreCase))
                return (prop.GetChild(2) as SyntaxTokenNode)?.Text
                       ?? prop.Values.OfType<SyntaxTokenNode>().FirstOrDefault()?.Text;
        return null;
    }

    // ---- Helpers -----------------------------------------------------------

    private static FormDeclarationSyntax? PrimaryForm(CompilationUnitSyntax root)
    {
        var forms = root.DescendantNodes<FormDeclarationSyntax>().ToList();
        return forms.FirstOrDefault(f => f.NameText.Equals("main", StringComparison.OrdinalIgnoreCase))
               ?? forms.FirstOrDefault();
    }

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

    private static string? RenderPartName(SyntaxNode renderItem)
    {
        // staff/tab items: the first bare identifier token after the keyword is
        // the part name (an optional clef/tuning token may precede or follow it,
        // but the part name is what a declared part matches).
        // Skip slot 0 (the staff/tab keyword); the part name is the first
        // identifier after it.
        var toks = renderItem.DescendantNodes().OfType<SyntaxTokenNode>().ToList();
        for (int i = 1; i < toks.Count; i++)
            if (toks[i].Kind == SyntaxKind.Identifier)
                return toks[i].Text;
        return toks.Count > 1 ? toks[1].Text : null;
    }

    private static void AppendToken(StringBuilder line, string tok, string indent)
    {
        if (line.Length > indent.Length) line.Append(' ');
        line.Append(tok);
    }

    private void FlushLine(StringBuilder line, string indent)
    {
        if (line.Length > indent.Length)
        {
            _sb.Append(line.ToString().TrimEnd()).Append('\n');
        }
        line.Clear();
        line.Append(indent);
    }

    private static string SanitizeVar(string name)
    {
        // LilyPond variable names are letters only (no digits/underscores).
        var sb = new StringBuilder();
        foreach (char c in name)
            if (char.IsLetter(c)) sb.Append(c);
        return sb.Length == 0 ? "music" : sb.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
