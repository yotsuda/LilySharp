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

        // One music variable per declared part. A part-major score keeps its
        // sections inside the part block; the form orders them.
        var partVars = new Dictionary<string, string>(StringComparer.Ordinal);
        if (parts.Count > 0)
        {
            foreach (var part in parts)
            {
                string varName = SanitizeVar(part.Name.Text);
                partVars[part.Name.Text] = varName;
                _anchorOctave = AnchorOctaveOf(part);
                EmitPartVariable(varName, OrderedMusic(part, form, sections), root);
            }
        }
        else
        {
            // No explicit part: treat the whole file's music stream as one voice.
            partVars["music"] = "music";
            EmitPartVariable("music", TopLevelMusic(root), root);
        }

        EmitScore(root, parts, partVars);
        return _sb.ToString();
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
        PartDeclarationSyntax part, FormDeclarationSyntax? form,
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
        var partSections = part.DescendantNodes<SectionDeclarationSyntax>().ToList();
        var byName = new Dictionary<string, SyntaxNode>(StringComparer.Ordinal);
        var inOrder = new List<SyntaxNode>();
        foreach (var s in allSections)
        {
            // `allSections` is a DESCENDANT walk of the whole file, so it already carries
            // both spellings in document order; a section belonging to some OTHER part
            // holds no PartBlock of ours and drops out.
            SyntaxNode? container = partSections.Contains(s)
                ? s
                : PartBlockBody(s.DescendantNodes<PartBlockSyntax>()
                    .FirstOrDefault(b => b.Name == part.Name.Text));
            if (container == null)
                continue;
            byName[s.SectionName] = container;
            inOrder.Add(container);
        }

        var result = new List<SyntaxNode>();
        if (form != null)
        {
            foreach (var name in FormSectionOrder(form))
                if (byName.TryGetValue(name, out var section))
                    result.AddRange(MusicItems(section));
        }
        else
        {
            foreach (var s in inOrder)
                result.AddRange(MusicItems(s));
        }

        // Also carry any part-level clef declared outside a section (e.g. a
        // mid-part clef change is inside a section and handled there).
        return result;
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

    private void EmitScore(CompilationUnitSyntax root, List<PartDeclarationSyntax> parts,
        Dictionary<string, string> partVars)
    {
        var render = root.DescendantNodes<RenderDeclarationSyntax>().FirstOrDefault();
        _sb.Append("\\score {\n");

        var staves = new List<string>();
        if (render != null)
        {
            foreach (var child in EnumerateChildren(render))
            {
                switch (child)
                {
                    case StaffRenderSyntax st:
                        staves.Add(EmitStaff(RenderPartName(st), parts, partVars, tab: false));
                        break;
                    case TabRenderSyntax tb:
                        staves.Add(EmitStaff(RenderPartName(tb), parts, partVars, tab: true));
                        break;
                }
            }
        }
        if (staves.Count == 0 && partVars.Count > 0)
        {
            // Fall back to a plain staff for the first part.
            var first = partVars.First();
            staves.Add(EmitStaff(first.Key, parts, partVars, tab: false));
        }

        if (staves.Count == 1)
        {
            _sb.Append(staves[0]);
        }
        else
        {
            _sb.Append("  \\new StaffGroup <<\n");
            foreach (var s in staves) _sb.Append(s);
            _sb.Append("  >>\n");
        }
        _sb.Append("  \\layout {}\n}\n");
    }

    private string EmitStaff(string? partName, List<PartDeclarationSyntax> parts,
        Dictionary<string, string> partVars, bool tab)
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
            sb.Append("    \\new TabStaff");
            if (tuning.Length > 0)
                sb.Append(" \\with { stringTunings = #").Append(tuning).Append(" }");
            sb.Append(" { \\").Append(varName).Append(" }\n");
        }
        else
        {
            sb.Append("    \\new Staff { ");
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
