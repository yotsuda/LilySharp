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
///
/// A preset the <c>.lys</c> DOES hold but LilyPond has no spelling for is EXPANDED, not
/// dropped: <c>instrument bass</c> becomes the clef, the relative anchor, the string
/// tuning and the sounding transposition it stands for (<see cref="PartClefWord"/>), the
/// way a degree chord becomes its pitches. Expanding what the source says is transpiling;
/// inventing what it never said is the re-derivation this file refuses.
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
    /// The running WRITTEN key signature (sharps positive, flats negative) and the tonic an
    /// omitted chord root anchors on — the two things a scale-degree member resolves against.
    /// </summary>
    /// <remarks>
    /// Seeded from the score's top-level key (<see cref="ScoreHomeKey"/>, the same reading the
    /// collector and the MIDI / MusicXML exporters take) and advanced by every <c>\key</c> this
    /// exporter WRITES, in emission order — which is the collector's order too, because
    /// <see cref="SectionHeaderMusic"/> already emits a section's own key where the collector
    /// applies it. A custom/atonal key has no tonic (<c>Valid</c> false), and the collector
    /// falls back to C there, so this does the same.
    /// ⚠️ It is the written key, NOT the sounding one: a part-level <c>transpose</c> property
    /// moves the key AND the pitches (MeasureCollector's PartTranspose.Read), and this
    /// exporter writes neither — a standing gate of its own, with one fixture in it
    /// (<c>test/transpose-score</c>). ⚠️ It is NOT the <c>instrument</c> gate, which is
    /// closed: see <see cref="PartClefWord"/>.
    /// </remarks>
    private int _keySharps;
    private KeyTonic _tonic = KeyTonic.CMajor;
    private int _homeKeySharps;
    private KeyTonic _homeTonic = KeyTonic.CMajor;

    /// <summary>
    /// The relative-octave frame, TWICE: where Lily# stands, and where the text this exporter
    /// has written puts LilyPond. Both are absolute octave numbers (4 = the octave of middle C).
    /// </summary>
    /// <remarks>
    /// They are the same number everywhere the transpiler is exact, and a chord is what parts
    /// them: the next event is relative to the chord's ANCHOR in Lily# (the root's bare letter,
    /// or the key's tonic when the root is omitted — MeasureCollector.ItemFactory) and to the
    /// chord's FIRST MEMBER in LilyPond (lily/music-sequence.cc:213-219, <c>ret_first</c>).
    /// A degree chord written <c>&lt;1' 3 5&gt;</c> sounds C5 E4 G4 and leaves Lily# on C4 —
    /// LilyPond, reading the C5 this exporter had to write first, would be an octave up.
    /// <para>
    /// The difference is carried, not warned about: the next pitch's marks absorb it
    /// (<see cref="EmitMusicPitch"/>), which puts both frames back on the same note. Only a
    /// degree chord can open the gap, so in every book without one the correction is 0 and
    /// every pitch token is still the source's, byte for byte.
    /// </para>
    /// <para>
    /// ⚠️ <see cref="_frameTracked"/> goes false where this exporter hands pitches to a
    /// sub-exporter whose frame it does not model (a grace body, a phrase reference's nested
    /// <c>\relative</c>, a voice span). A degree chord after that point is reported rather
    /// than trusted.
    /// </para>
    /// </remarks>
    private int _lysStep, _lysOctave;
    private int _lyStep, _lyOctave;
    private bool _frameTracked = true;

    /// <summary>
    /// The parts whose music is drum-kit music, and whether the variable being written now is
    /// one of them. A drum note (<c>hh8 bd4</c>) is a NAME, not a pitch, and LilyPond only
    /// reads those names inside <c>\drummode</c> — so the part is wrapped in that instead of
    /// <c>\relative</c>, and its staff is a <c>DrumStaff</c>.
    /// </summary>
    /// <remarks>
    /// The vocabulary itself needs no translation: Lily#'s drum names and aliases ARE
    /// LilyPond's (DrumNameRegistry cites ly/drumpitch-init.ly drumPitchNames), so the token
    /// goes through verbatim like any other. Before this, all 24 of them in the corpus were
    /// dropped with a warning and test/drum-groove's twin was a bar-check failure.
    /// </remarks>
    private readonly HashSet<string> _drumParts = new(StringComparer.Ordinal);
    private bool _drumMode;

    /// <summary>The note value Lily# would give an event that writes no duration — its own
    /// rule, not LilyPond's. See <see cref="EmitEventDuration"/>.</summary>
    private string _lastWrittenValue = "4";

    /// <summary>Set when the next event must write its duration out because LilyPond would
    /// otherwise infer a different one. See <see cref="EmitEventDuration"/>.</summary>
    private bool _forceNextDuration;

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

        // The key a part starts in, before any section header or mid-stream change.
        _homeTonic = ScoreHomeKey.Read(root);
        _homeKeySharps = ScoreHomeKey.Sharps(root);

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
                var music = OrderedMusic(name, part, form, sections);
                if (IsDrumPart(name, music))
                {
                    _drumParts.Add(name);
                    // A drummap block re-tables position / notehead / MIDI key for the score
                    // (DrumOverrides). LilyPond spells that as drumPitchTable and
                    // drumStyleTable overrides, which this transpiler does not write — so the
                    // twin plays the DEFAULT kit and is a different page wherever the map bit.
                    if (root.DescendantNodes<DrummapDeclarationSyntax>().Any())
                        _warnings.Add(
                            "drummap { } is not exported — the twin uses LilyPond's default "
                            + "drum table, so any remapped position, notehead or MIDI key differs");
                }
                _drumMode = _drumParts.Contains(name);
                EmitPartVariable(varName, music, root);
                _drumMode = false;
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
            CarryFrameInto(buf);
            // Both sides open a FRESH frame for the body — LilyPond the nested \relative
            // written below, Lily# its EnterDefaultFrame — at the part's anchor moved by the
            // reference's own marks.
            buf._lysStep = buf._lyStep = 0;
            buf._lysOctave = buf._lyOctave = _anchorOctave + v.OctaveOffset;
            buf.EmitMusicStream(MusicItems(body).ToList(), "");
            _warnings.AddRange(buf._warnings);
            string inner = buf._sb.ToString().Replace("\n", " ").Trim();
            // The nested \relative the reference opens is where the two frames part company
            // (the warning above says so); stop tracking rather than guess.
            _frameTracked = false;
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
    /// The same precedence MeasureCollector applies (MeasureCollector.cs GetPartDefaults →
    /// <c>partOctave ?? InstrumentDefaults.GetDefaultOctave(ParseClefType(clef))</c>, where
    /// <c>partOctave</c> is the explicit <c>octave N</c> property or, failing that, the
    /// <c>instrument</c> preset's own octave), read off the same part properties through the
    /// same table.
    /// <para>
    /// ⚠️ The preset's octave beats the CLEF's default even when a clef is written too —
    /// <c>instrument flute</c> anchors at octave 5 while <c>GetDefaultOctave(Treble)</c> is
    /// 4 — because <c>resolvedOctave ??= defaultOctave</c> runs after the clef is resolved.
    /// Mirrored here rather than approximated: an anchor that is off by an octave is a twin
    /// that plays other pitches.
    /// </para>
    /// <para>
    /// ⚠️ An <c>instrument</c> preset is a BUNDLE — <c>instrument bass</c> means bass clef
    /// AND octave 3 AND a sounding pitch an octave down (InstrumentDefaults.GetTransposition;
    /// an electric bass is written an octave above where it sounds, and that −12 is
    /// deliberate). It is read WHOLE or not at all: taking the octave third of it and leaving
    /// the other two would move the twin's written pitch while its sounding pitch stayed
    /// wrong, i.e. make it wrong in a way that LOOKS right. See <see cref="PartClefWord"/>
    /// for why reading it is still a transpilation and not a re-derivation.
    /// </para>
    /// </remarks>
    private static int AnchorOctaveOf(PartDeclarationSyntax part)
    {
        string? octave = PartProperty(part, "octave");
        if (octave != null && int.TryParse(octave, out int explicitOctave))
            return explicitOctave;
        if (InstrumentPresetOf(part) is string preset)
            return InstrumentDefaults.GetDefaults(preset).Octave;
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

    /// <summary>
    /// Whether a part's music is DRUM music — it names drum instruments rather than pitches.
    /// </summary>
    /// <remarks>
    /// LilyPond's two vocabularies do not mix in one stream: inside <c>\drummode</c> a
    /// <c>c</c> is not a pitch, and outside it <c>hh</c> is not a drum. Lily# has no such
    /// mode — a bare identifier is a drum name wherever the registry knows it — so a part
    /// that writes both cannot be spelled at all, and saying so is better than writing a
    /// <c>.ly</c> LilyPond refuses to read.
    /// </remarks>
    private bool IsDrumPart(string partName, List<SyntaxNode> music)
    {
        bool drums = false, pitched = false;
        foreach (var item in music)
        {
            foreach (var n in item.DescendantNodes().Prepend(item))
            {
                switch (n)
                {
                    case DrumNoteSyntax: drums = true; break;
                    case NoteSyntax: pitched = true; break;
                    case ChordSyntax c:
                        if (c.DrumNames.Any()) drums = true;
                        if (c.Pitches.Any() || c.Degrees.Any()) pitched = true;
                        break;
                }
            }
        }
        if (drums && pitched)
        {
            _warnings.Add(
                $"part '{partName}' writes drum names and pitches in one stream, which "
                + "LilyPond's \\drummode cannot hold — the drum notes are dropped");
            return false;
        }
        return drums;
    }

    private void EmitPartVariable(string varName, List<SyntaxNode> music, CompilationUnitSyntax root)
    {
        // ⚠️ The two modes anchor DIFFERENTLY on purpose. Absolute octave is middle C
        //   whatever the clef (OctaveContext: "clef default is deliberately NOT used here"),
        //   so \fixed is always c'; relative follows the part's own default octave.
        // A drum part has no octave to anchor at all — its notes are names.
        string wrapper = _drumMode
            ? "\\drummode"
            : _octaveAbsolute
            ? "\\fixed c'"
            : "\\relative " + AnchorPitch(_anchorOctave);
        _sb.Append(varName).Append(" = ").Append(wrapper).Append(" {\n");

        // Each part starts from Lily#'s own default duration, as the collector does
        // (MeasureCollector resets _defaultDuration to a quarter per part).
        _lastWrittenValue = "4";
        _forceNextDuration = false;

        // …and from its own octave frame and the score's home key, for the same reason
        // (MeasureCollector.cs sets LastPitchName = 'c' and re-arms the ambient tonic per
        // voice). The wrapper this method just wrote IS the frame: `\relative c'` starts
        // both sides on c at the anchor octave.
        _lysStep = _lyStep = 0;
        _lysOctave = _lyOctave = _anchorOctave;
        _frameTracked = true;
        _keySharps = _homeKeySharps;
        _tonic = _homeTonic;

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
        DrumNoteSyntax dn => EmitDrumNote(dn),
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

    /// <summary>
    /// The duration to WRITE for an event, and the two pieces of state that decide it.
    /// </summary>
    /// <remarks>
    /// An omitted duration is not the same thing on the two sides once a grace has gone by.
    /// LILYPOND-REF: lily/parser.yy:3503-3515 maybe_notemode_duration / optional_notemode_duration
    ///   — a written duration becomes <c>parser-&gt;default_duration_</c> and an omitted one is
    ///   replaced by it. That is PARSER state (lily/include/lily-parser.hh:44), which is why it
    ///   reaches straight through a grace body.
    /// LilyPond repeats the last duration it READ, and it read the grace body
    /// (<c>\grace { d8 } c</c> makes that <c>c</c> an EIGHTH); Lily# collects the grace with
    /// its own local default (MeasureCollector.CollectGraceNotes' graceDefaultDuration) which
    /// never escapes, so its <c>c</c> is whatever the main stream last said — a QUARTER at the
    /// head of a piece. The twin was then a different piece of music, and LilyPond said so:
    /// test/ossia-beams failed its bar check at 7/8.
    /// <para>
    /// A DOT parts them the same way, and in the other direction: Lily# carries the note VALUE
    /// and drops the dots, LilyPond carries the duration whole. So <c>c4. d</c> is 5/8 of music
    /// on the page and 6/8 in the twin — and in 6/8 that twin's bar is COMPLETE, so LilyPond
    /// has nothing to say about it. Measured 2026-08-01: <c>c'4. d'</c> and <c>c'4. d'4</c>
    /// draw the same six glyphs and raise the same short-measure LYS2006, while
    /// <c>c'4. d'4.</c> draws seven. ⇒ the event after a DOTTED one writes its value out too.
    /// </para>
    /// <para>
    /// Those two cases aside, everything keeps copying the source, because a transpiler that
    /// re-spells durations everywhere is much harder to read against the .lys it came from.
    /// </para>
    /// <para>
    /// ⚠️ <see cref="_lastWrittenValue"/> mirrors Lily#'s rule and not LilyPond's: the note
    /// VALUE only, dots dropped (MeasureCollector.ItemFactory
    /// <c>_defaultDuration = Fraction.FromNoteValue(noteValue)</c>), reset to a quarter per
    /// part variable. It is fed only by the main stream — the grace body is emitted by its own
    /// exporter instance, which is exactly why its durations do not leak in here either.
    /// </para>
    /// </remarks>
    private string EmitEventDuration(DurationSyntax? d)
    {
        if (d != null)
        {
            _lastWrittenValue = d.NumberToken.Text;
            // Lily#'s carry is the value alone, so the next event has to be told the value
            // whenever this one wrote dots LilyPond would carry with it.
            _forceNextDuration = d.DotCount > 0;
            return EmitDuration(d);
        }
        if (!_forceNextDuration)
            return "";
        _forceNextDuration = false;
        return _lastWrittenValue;
    }

    private string EmitNote(NoteSyntax n)
    {
        var (prefix, suffix) = SplitAttachments(n.Articulations);
        string trem = n.Tremolo is { } t ? t.Text : "";
        return prefix + EmitMusicPitch(n.Pitch) + EmitEventDuration(n.Duration) + trem + suffix;
    }

    /// <summary>
    /// A drum note (<c>hh8</c>, <c>bd4</c>) — the name verbatim, because Lily#'s drum
    /// vocabulary IS LilyPond's (see <see cref="_drumParts"/>).
    /// </summary>
    private string EmitDrumNote(DrumNoteSyntax d)
    {
        if (!_drumMode)
        {
            // Reached through a phrase reference the part scan did not follow, or from a part
            // that also writes pitches. Either way \drummode is not open and the name would
            // be read as something else entirely.
            _warnings.Add($"drum note '{d.DrumName}' is outside \\drummode and was dropped");
            return "";
        }
        var (prefix, suffix) = SplitAttachments(d.Articulations);
        string trem = d.Tremolo is { } t ? t.Text : "";
        return prefix + d.DrumName + EmitEventDuration(d.Duration) + trem + suffix;
    }

    /// <summary>
    /// A pitch in the music stream: the source's own token, and the octave frames advanced
    /// past it. See <see cref="_lysStep"/> for why the marks are not always the source's.
    /// </summary>
    private string EmitMusicPitch(PitchSyntax p)
    {
        // \fixed has no frame: every mark is an absolute offset from the wrapper's c'.
        if (_octaveAbsolute)
            return EmitPitch(p);

        int step = RelativeOctave.StepIndex(p.PitchName[0]);
        int source = p.OctaveOffset;
        int lys = RelativeOctave.Resolve(_lysStep, _lysOctave, step, source);
        // What LilyPond would do with the source's own marks, and what it takes to land on
        // Lily#'s note instead. The two agree — and this is source + 0 — unless a degree
        // chord has left the frames apart.
        int written = source + lys - RelativeOctave.Resolve(_lyStep, _lyOctave, step, source);
        _lysStep = _lyStep = step;
        _lysOctave = _lyOctave = lys;
        return p.PitchToken.Text + OctaveMarks(written);
    }

    /// <summary>
    /// Hands a nested body's exporter the state a pitch resolves against — the two octave
    /// frames and the running key — so a body emitted into a temporary buffer sees what the
    /// stream around it sees.
    /// </summary>
    private void CarryFrameInto(LilyPondExporter buf)
    {
        buf._drumMode = _drumMode;
        buf._lysStep = _lysStep;
        buf._lysOctave = _lysOctave;
        buf._lyStep = _lyStep;
        buf._lyOctave = _lyOctave;
        buf._frameTracked = _frameTracked;
        buf._keySharps = _keySharps;
        buf._tonic = _tonic;
        buf._homeKeySharps = _homeKeySharps;
        buf._homeTonic = _homeTonic;
    }

    /// <summary>
    /// Takes the state back out of a body that is plain sequential music on BOTH sides (a
    /// tuplet, a repeat) — the stream continues where the body left off. Bodies whose frame
    /// the two engines hand over differently (a grace, a voice span, a phrase reference) do
    /// not call this; they clear <see cref="_frameTracked"/> instead.
    /// </summary>
    private void CarryFrameBack(LilyPondExporter buf)
    {
        _lysStep = buf._lysStep;
        _lysOctave = buf._lysOctave;
        _lyStep = buf._lyStep;
        _lyOctave = buf._lyOctave;
        _frameTracked = buf._frameTracked;
        _keySharps = buf._keySharps;
        _tonic = buf._tonic;
    }

    /// <summary>Octave marks for a net shift: <c>'</c> up, <c>,</c> down.</summary>
    private static string OctaveMarks(int offset)
        => offset > 0 ? new string('\'', offset)
         : offset < 0 ? new string(',', -offset)
         : "";

    /// <summary>
    /// A resolved (step, alteration) as a LilyPond pitch name — the same suffixes the parser
    /// spells (<see cref="KeySpelling.SpellLetter"/>): <c>fis</c>, <c>bes</c>, <c>ees</c>.
    /// </summary>
    private string SpellPitch(int step, int alteration)
    {
        char letter = "cdefgab"[step];
        if (alteration is < -2 or > 2)
            _warnings.Add(
                $"a scale degree resolved to {Math.Abs(alteration)} accidentals on {letter}, "
                + "which LilyPond's note names do not spell — written as a double");
        int n = Math.Clamp(alteration, -2, 2);
        return letter + (n > 0 ? string.Concat(Enumerable.Repeat("is", n))
                       : n < 0 ? string.Concat(Enumerable.Repeat("es", -n))
                       : "");
    }

    private string EmitRest(RestSyntax r)
    {
        var (prefix, suffix) = SplitAttachments(r.Articulations);
        string mmr = r.IsMultiMeasure ? "*" + r.MeasureCount : "";
        return prefix + r.RestToken.Text + EmitEventDuration(r.Duration) + mmr + suffix;
    }

    /// <summary>
    /// A chord. Lily#'s chord-level octave marks go AFTER the <c>&gt;</c>
    /// (<c>&lt;d f a&gt;,</c> = the whole chord down an octave); LilyPond has no such
    /// spelling and rejects it outright (<c>syntax error, unexpected ','</c>), so the
    /// shift is pushed onto the members.
    /// </summary>
    /// <remarks>
    /// WHICH members depends on the mode this variable is wrapped in, and the two answers
    /// are different:
    /// <list type="bullet">
    /// <item><c>\fixed</c> — every pitch stands on its own against the reference, so every
    /// one of them carries the shift.</item>
    /// <item><c>\relative</c> — inside a chord each pitch is octaved against the PREVIOUS
    /// member, so shifting the first one carries the rest with it; adding the marks to all
    /// of them would move member N by N octaves.</item>
    /// </list>
    /// LILYPOND-REF: lily/music-sequence.cc:142-160 music_list_to_relative — walks the
    ///   members CHAINING <c>last = m-&gt;to_relative_octave (last)</c>, so member N is
    ///   octaved against member N-1, and returns the FIRST member when ret_first.
    /// LILYPOND-REF: lily/music-sequence.cc:213-219 event_chord_relative_callback — an
    ///   EventChord calls that with ret_first true, which is also why the chord's first
    ///   note is what the NEXT event octaves against
    ///   (scm/define-music-types.scm:268-269 wires the to-relative-callback).
    /// </remarks>
    private string EmitChord(ChordSyntax c)
    {
        int off = c.ChordOctaveOffset;
        string marks = OctaveMarks(off);
        bool hasDegrees = c.Degrees.Any();
        if (hasDegrees && !_frameTracked)
            _warnings.Add(
                "a degree chord follows a phrase reference, whose nested \\relative leaves the "
                + "octave frame with a different answer on each side — check its octave by hand");

        // LilyPond's chain WITHIN the chord: each member is octaved against the one written
        // before it, so what a degree has to write depends on its neighbour, not on the root.
        int chainStep = _lyStep, chainOctave = _lyOctave;
        int firstStep = -1, firstOctave = 0;

        // Lily#'s anchor: the root's LETTER resolved bare in the incoming frame, plus the
        // whole-chord marks. The root's OWN marks are local to its sounding pitch — the anchor
        // is what the degrees stack on and what the next event is relative to
        // (MeasureCollector.ItemFactory CreateChordItem).
        int anchorStep = _lysStep, anchorOctave = _lysOctave;
        if (c.Root is { } root)
        {
            anchorStep = RelativeOctave.StepIndex(root.PitchName[0]);
            anchorOctave = _octaveAbsolute
                ? AbsoluteBaseOctave + root.OctaveOffset + off
                : RelativeOctave.Resolve(_lysStep, _lysOctave, anchorStep, 0) + off;
        }
        else if (hasDegrees)
        {
            // Omitted root (<1 3 5>): degree 1 is the KEY'S TONIC, anchored in the frame as a
            // written root would be. A custom/atonal key has no tonic, so C — the collector's
            // own fallback.
            anchorStep = _tonic.Valid ? _tonic.Step : 0;
            anchorOctave = _octaveAbsolute
                ? AbsoluteBaseOctave + off
                : RelativeOctave.Resolve(_lysStep, _lysOctave, anchorStep, 0) + off;
        }

        var sb = new StringBuilder("<");
        bool first = true;
        foreach (var p in c.Pitches)
        {
            if (!first) sb.Append(' ');
            if (first && !_octaveAbsolute)
            {
                // The root, written where Lily# sounds it: the anchor plus its own marks.
                int want = anchorOctave + p.OctaveOffset;
                sb.Append(p.PitchToken.Text)
                  .Append(OctaveMarks(want - RelativeOctave.Resolve(chainStep, chainOctave, anchorStep, 0)));
                chainStep = firstStep = anchorStep;
                chainOctave = firstOctave = want;
            }
            else
            {
                // Absolute: every member carries the whole-chord shift. Relative: the members
                // after the root keep the source's marks — Lily# STACKS them on the root while
                // LilyPond CHAINS them member to member, a deliberate Lily# divergence
                // (MeasureCollector.ItemFactory) this transpiler does not try to spell away.
                sb.Append(EmitPitch(p));
                if (_octaveAbsolute) sb.Append(marks);
                else
                {
                    int step = RelativeOctave.StepIndex(p.PitchName[0]);
                    chainOctave = RelativeOctave.Resolve(chainStep, chainOctave, step, p.OctaveOffset);
                    chainStep = step;
                }
            }
            first = false;
        }

        // Scale-degree members (<d 3 5 7,>, <1 3 5>): resolved here, because LilyPond has no
        // spelling for a degree at all — it was the last thing this exporter dropped in
        // silence, and `<>` is a zero-length event, so test/chord-octave-marks failed its bar
        // check at 1/4 and read as a book with no beams. Same call the collector makes.
        foreach (var degree in c.Degrees)
        {
            if (!first) sb.Append(' ');
            var (step, alteration, octave) = ChordDegrees.Resolve(
                anchorStep, anchorOctave, degree.Number, degree.Alteration,
                degree.OctaveOffset, _keySharps);
            int written = _octaveAbsolute
                ? octave - AbsoluteBaseOctave
                : octave - RelativeOctave.Resolve(chainStep, chainOctave, step, 0);
            sb.Append(SpellPitch(step, alteration)).Append(OctaveMarks(written));
            if (first) { firstStep = step; firstOctave = octave; }
            chainStep = step;
            chainOctave = octave;
            first = false;
        }

        // Drum members (<bd hh>): names, like a bare drum note, and only inside \drummode.
        foreach (var drum in c.DrumNames)
        {
            if (!_drumMode)
            {
                _warnings.Add(
                    $"drum chord member '{drum.DrumName}' is outside \\drummode and was dropped");
                continue;
            }
            if (!first) sb.Append(' ');
            sb.Append(drum.DrumName);
            first = false;
        }

        // Where the two sides stand now: Lily# on the chord's anchor, LilyPond on its first
        // member. Equal for an ordinary chord; a degree chord can part them (see _lysStep).
        if (!_octaveAbsolute && firstStep >= 0)
        {
            _lysStep = anchorStep;
            _lysOctave = anchorOctave;
            _lyStep = firstStep;
            _lyOctave = firstOctave;
        }

        sb.Append('>');
        sb.Append(EmitEventDuration(c.Duration));
        var (prefix, suffix) = SplitAttachments(c.Articulations);
        return prefix + sb.ToString() + suffix;
    }

    /// <summary>
    /// The octave a bare letter means in absolute mode — the <c>\fixed c'</c> this exporter
    /// wraps every absolute part in, i.e. the octave of middle C.
    /// </summary>
    /// <remarks>
    /// ⚠️ Lily#'s own absolute anchor is the part's <c>octave N</c> when it states one
    /// (OctaveContext.OctaveBase), and this exporter writes <c>\fixed c'</c> regardless — an
    /// existing gate, not a new one (see the class remarks). Resolving degrees against the
    /// wrapper that was actually written keeps them consistent with the verbatim pitches
    /// beside them instead of correct on their own.
    /// </remarks>
    private const int AbsoluteBaseOctave = 4;

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

    /// <summary>
    /// A key signature — and the running key a scale-degree chord stacks in, advanced here
    /// because this is the one place a key is WRITTEN (the file's own settings, a section's
    /// header, and a mid-stream change all come through it, in emission order).
    /// </summary>
    private string EmitKey(KeySignatureSyntax k)
    {
        _tonic = KeyTonic.Of(k);
        // MeasureCollector.CalculateKeySharps — PitchName, which carries the accidental
        // suffix and normalizes LilyPond's `es`/`as` contractions the table does not hold.
        _keySharps = k.IsCustom ? 0 : KeySpelling.SharpsFor(k.Pitch.PitchName, k.Mode.Text) ?? 0;
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
        CarryFrameInto(buf);
        buf.EmitMusicStream(MusicItems(tup.Body).ToList(), "");
        // A tuplet is plain sequential music on both sides: its notes are in the enclosing
        // frame and the note after it follows the tuplet's last, so the frame comes back.
        CarryFrameBack(buf);
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
        // The two sides read a span completely differently, so this is where the octave
        // frames earn their keep:
        //   Lily# — every branch reads from the frame the span OPENED in, and so does the
        //     music after it: a span is simultaneous music and moves nothing
        //     (MeasureCollector's _parallelSpans).
        //   LilyPond — the branches CHAIN into one another and the span hands out the LAST
        //     one's pitch. Measured with `c4 c c c << { c''1 } \\ { c,,,1 } >> c1`, which
        //     reads C4 C4 C4 C4 / C6 / C3 / C3: branch 2 is octaved against branch 1's end,
        //     and the note after the span against branch 2's.
        // So each branch is emitted with Lily#'s frame set to the span's and LilyPond's set
        // to wherever the previous branch left it; every first pitch then absorbs the
        // difference by itself (EmitMusicPitch), and so does the first pitch after the span.
        int spanStep = _lysStep, spanOctave = _lysOctave;
        int chainStep = _lyStep, chainOctave = _lyOctave;

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
            CarryFrameInto(buf);
            buf._lysStep = spanStep;
            buf._lysOctave = spanOctave;
            buf._lyStep = chainStep;
            buf._lyOctave = chainOctave;
            buf.EmitMusicStream(MusicItems(block).ToList(), "");
            chainStep = buf._lyStep;
            chainOctave = buf._lyOctave;
            _frameTracked &= buf._frameTracked;
            _warnings.AddRange(buf._warnings);
            string body = buf._sb.ToString().Replace("\n", " ").Trim();
            if (body.Length > 0)
                bodies.Add(body);
        }

        _lysStep = spanStep;
        _lysOctave = spanOctave;
        _lyStep = chainStep;
        _lyOctave = chainOctave;

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
        CarryFrameInto(buf);
        // The grace body has its OWN default duration, an eighth, and it is Lily#'s own rule
        // (MeasureCollector.CollectGraceNotes graceDefaultDuration = Fraction.Eighth;
        // LilyPond has no grace-specific default and would take whatever the main stream last
        // wrote). So the body's first event writes its value out unless it states one, and the
        // events after it inherit — which is the same carry on both sides from there on.
        buf._lastWrittenValue = "8";
        buf._forceNextDuration = true;
        buf.EmitMusicStream(MusicItems(g.Body).ToList(), "");
        // ⚠️ The OCTAVE frame does NOT leak the way the duration does: the grace body advances
        // it on BOTH sides, so it carries out like a tuplet's. MeasureCollector.CollectGraceNotes
        // writes _octave.CurrentOctave per grace note and never restores it (the save/restore
        // OctaveContext.Snapshot mentions is the parallel span's, not this). Measured, because
        // the comment here used to claim the opposite: `a4 grace { e8 } c4` renders A3 E3 C3,
        // and its twin reads A3 E3 C3 in LilyPond.
        CarryFrameBack(buf);
        _warnings.AddRange(buf._warnings);
        string body = buf._sb.ToString().Replace("\n", " ").Trim();
        // LilyPond carries the grace body's last duration out to the next event; Lily# does
        // not. See EmitEventDuration.
        _forceNextDuration = true;
        return $"{kw} {{ {body} }}";
    }

    private string EmitRepeat(RepeatExpressionSyntax rep)
    {
        string type = rep.RepeatType.Text;
        string count = rep.Count.Text;
        var buf = new LilyPondExporter
        { _octaveAbsolute = _octaveAbsolute, _anchorOctave = _anchorOctave };
        CarryFrameInto(buf);
        buf.EmitMusicStream(MusicItems(rep.Body).ToList(), "");
        // The body is WRITTEN once and read once by the relative pass on both sides, however
        // many times it is played, so its frame carries out like a tuplet's.
        CarryFrameBack(buf);
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
                        rows.Add(EmitStaff(RenderPartName(tb), parts, partVars, tab: true, "    ",
                            tabNumbersOnly: TabIsNumbersOnly(tb)));
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
        // The ossia's own clef word (`ossia bass melody`) when it has one, else the part's —
        // which is its `clef` property or the one its `instrument` implies, the same two the
        // page reads (RenderSpecParser.ParseOssia → GetPartClef).
        // ⚠️ An explicit clef is written even though firstClef suppresses the OPENING one:
        // the glyph stays hidden, but the notes still have to be READ in that clef.
        string? clef = OssiaClef(ossia)
                       ?? PartClefWord(parts.FirstOrDefault(p => p.Name.Text == partName));
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

    /// <summary>
    /// True for <c>tab part as numbers</c> — fret digits only. Mirrors
    /// <c>RenderSpecParser</c>'s reading of the same trailing <c>as …</c> selector; the two
    /// must agree, or the twin is drawn in the other mode from the page.
    /// </summary>
    private static bool TabIsNumbersOnly(TabRenderSyntax tab)
    {
        var toks = tab.DescendantNodes().OfType<SyntaxTokenNode>().ToList();
        for (int i = 1; i < toks.Count - 1; i++)
            if (string.Equals(toks[i].Text, "as", StringComparison.Ordinal))
                return string.Equals(toks[i + 1].Text, "numbers", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private string EmitStaff(string? partName, List<PartDeclarationSyntax> parts,
        Dictionary<string, string> partVars, bool tab, string indent,
        bool tabNumbersOnly = false)
    {
        string varName = partName != null && partVars.TryGetValue(partName, out var v)
            ? v : partVars.Values.FirstOrDefault() ?? "music";
        var part = parts.FirstOrDefault(p => p.Name.Text == partName)
                   ?? (parts.Count == 1 ? parts[0] : null);
        string? clef = PartClefWord(part);

        var sb = new StringBuilder();
        if (tab)
        {
            string tuning = TabTuning(part);
            sb.Append(indent).Append("\\new TabStaff");
            if (tuning.Length > 0)
                sb.Append(" \\with { stringTunings = #").Append(tuning).Append(" }");
            sb.Append(" { ");
            // ⚠️ The two engines' DEFAULTS are opposite ends of the same switch. A bare
            // LilyPond TabStaff prints fret digits ALONE — it omits Stem, Beam, Flag, Dots,
            // Rest and TupletBracket (ly/engraver-init.ly TabStaff / `\tabFullNotation` in
            // ly/property-init.ly) — and that is Lily#'s `tab part as numbers`. Lily#'s
            // DEFAULT `tab part` draws the rhythm, so its twin has to ask for it back.
            // Measured: without this the twin of `tab-beam-script` held TWO Beam grobs (the
            // notation staff's) against the page's four, so every tab book was uncomparable
            // on beams and was written off as a frame problem in the sweep.
            if (!tabNumbersOnly)
                sb.Append("\\tabFullNotation ");
            // ⚠️ LilyPond pitches are SOUNDING; Lily# writes DISPLAY pitches and recovers the
            // sounding octave when it frets (Tunings.SoundingShift, read by
            // TabResolver.ResolveTabStrings). Written verbatim, the twin frets the DISPLAY
            // pitch and lands somewhere else entirely: `tab-percent-repeat` fingered
            // 17 0 17 5 5 17 0 17 against the page's 5 3 5 3 3 5 3 5, because A2 written is
            // A1 sounding. The shift is asked of the same table the page uses, so the two
            // cannot drift.
            AppendTabTranspose(sb, part, partName);
            sb.Append('\\').Append(varName).Append(" }\n");
        }
        else if (partName != null && _drumParts.Contains(partName))
        {
            // A DrumStaff is what reads \drummode: it carries the percussion clef, the
            // drum-kit notehead table and the position table, which is where the part's
            // `clef percussion` and Lily#'s DrumNameRegistry placements both come from
            // (LILYPOND-REF: ly/engraver-init.ly DrumStaff, ly/drumpitch-init.ly drums-style).
            // No \clef is written: the context's own is that clef, and a second one would be
            // this exporter inventing a convention.
            sb.Append(indent).Append("\\new DrumStaff { \\").Append(varName).Append(" }\n");
        }
        else
        {
            sb.Append(indent).Append("\\new Staff { ");
            if (clef != null) sb.Append("\\clef ").Append(clef).Append(' ');
            sb.Append('\\').Append(varName).Append(" }\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// The tuning Lily# frets this part against: its explicit <c>tuning</c> property, else
    /// the one its <c>instrument</c> preset implies, else guitar.
    /// </summary>
    /// <remarks>
    /// The page's own precedence (RenderSpecParser.ParseTab → <c>explicit ?? property ??
    /// InstrumentDefaults.GetTuning(preset)</c>, unknown/none = guitar), asked of the same
    /// table. ⚠️ It used to fall back to BASS while the page fell back to GUITAR, so a part
    /// naming neither tuning nor instrument got a four-string bass in the twin and six
    /// strings on the page (<c>test/tab-part-key</c>) — and after the twin started writing
    /// the tab's transposition, the same wrong default moved its pitches too.
    /// ⚠️ STILL NOT READ: the render item's own tuning modifier (<c>tab bass melody</c>),
    /// which outranks both on the page. No fixture writes it, and reading it here means
    /// re-deriving the token stripping ParseTab does (<c>as numbers</c>, <c>with chords</c>);
    /// it is a known gate, not an oversight.
    /// </remarks>
    private static Syntax.TuningType TabTuningType(PartDeclarationSyntax? part)
        => ((part != null ? PartProperty(part, "tuning")?.ToLowerInvariant() : null)
            ?? InstrumentDefaults.GetTuning(InstrumentPresetOf(part))) switch
        {
            "bass" => Syntax.TuningType.Bass,
            "bass5" => Syntax.TuningType.Bass5,
            "bass6" => Syntax.TuningType.Bass6,
            "ukulele" or "uke" => Syntax.TuningType.Ukulele,
            _ => Syntax.TuningType.Guitar,
        };

    // The LilyPond predefined tuning name for that tuning.
    private static string TabTuning(PartDeclarationSyntax? part) => TabTuningType(part) switch
    {
        Syntax.TuningType.Bass5 => "bass-five-string-tuning",
        Syntax.TuningType.Bass6 => "bass-six-string-tuning",
        Syntax.TuningType.Guitar => "guitar-tuning",
        Syntax.TuningType.Ukulele => "ukulele-tuning",
        _ => "bass-four-string-tuning",
    };

    /// <summary>
    /// Writes the written→sounding transposition the tab frets against, so the twin's fret
    /// numbers are the page's. Asked of <see cref="Tablature.Tunings"/> — the same table
    /// <c>TabResolver</c> reads — rather than restated here.
    /// </summary>
    /// <remarks>
    /// Only whole octaves are written (<c>\transpose c c,</c>), which is every shift the
    /// tunings and clefs produce. Anything else is REPORTED rather than dropped: a twin
    /// silently fretting other pitches is the shape that hid this hole in the first place.
    /// </remarks>
    private void AppendTabTranspose(StringBuilder sb, PartDeclarationSyntax? part, string? partName)
    {
        var clef = ClefFromName(PartClefWord(part));
        int shift = Tablature.Tunings.ClefOctaveShift(clef)
                    + PartTransposition(part);
        if (shift == 0)
            return;
        if (shift % 12 != 0)
        {
            _warnings.Add($"tab part '{partName ?? "?"}' sounds {shift} semitones from what is "
                          + "written, which is not a whole octave — the twin frets the written "
                          + "pitch and its fret numbers will not be the score's");
            return;
        }
        sb.Append("\\transpose c ").Append('c')
          .Append(new string(shift < 0 ? ',' : '\'', Math.Abs(shift) / 12)).Append(' ');
    }

    /// <summary>
    /// The part's <c>instrument</c> PRESET — the bare words, lowercased — or null when the
    /// part declares no instrument (or only a quoted display label).
    /// </summary>
    /// <remarks>
    /// ⚠️ Every value token is joined, not just the first: a hyphenated preset
    /// (<c>electric-bass</c>) is word+minus+word in the green tree, so
    /// <see cref="PartProperty"/> alone would read "electric" and fall through to the
    /// defaults. This is the reading MeasureCollector.GetPartDefaults takes, through the
    /// same <c>SplitInstrument</c>.
    /// </remarks>
    private static string? InstrumentPresetOf(PartDeclarationSyntax? part)
    {
        if (part == null) return null;
        foreach (var prop in part.Properties)
        {
            if (!prop.NameToken.Text.Equals("instrument", StringComparison.OrdinalIgnoreCase))
                continue;
            var texts = new List<string>();
            for (int vi = 2; vi < prop.SlotCount; vi++)
                if (prop.GetChild(vi) is SyntaxTokenNode vt)
                    texts.Add(vt.Text);
            if (texts.Count == 0) return null;
            string preset = InstrumentDefaults.SplitInstrument(texts).Preset;
            return preset.Length == 0 ? null : preset.ToLowerInvariant();
        }
        return null;
    }

    /// <summary>
    /// The clef word this part reads in: its explicit <c>clef</c> property, else the clef its
    /// <c>instrument</c> preset implies, else null (nothing to write — LilyPond's own default
    /// is treble, and so is Lily#'s).
    /// </summary>
    /// <remarks>
    /// The page's precedence, through the same table (RenderSpecParser.GetPartClef,
    /// MeasureCollector.GetPartDefaults → <c>resolvedClef ??=
    /// InstrumentDefaults.ClefWord(GetDefaults(preset).Clef)</c>).
    /// <para>
    /// ⚠️ Reading <c>instrument</c> is still a TRANSPILATION, not the re-derivation this file
    /// refuses. The distinction is whether the <c>.lys</c> HOLDS the thing: an instrument
    /// preset is written down in the source, LilyPond simply has no spelling for it, so it is
    /// expanded into the spellings LilyPond does have — the same move degree chords needed
    /// (ChordDegrees.Resolve). What stays refused is inventing what the source never said: a
    /// phrase's auto-transpose, an interval argument, a hand <c>.ly</c>'s comments.
    /// </para>
    /// <para>
    /// ⚠️ Read WHOLE: the clef here, the octave in <see cref="AnchorOctaveOf"/>, the tuning in
    /// <see cref="TabTuningType"/> and the sounding shift in <see cref="PartTransposition"/>
    /// all come off the same preset. Any one of them alone makes the twin wrong in a way that
    /// looks right. Until this was read, ten fixtures declaring <c>instrument bass</c> and no
    /// <c>clef</c> exported a treble twin against a bass page (docs/HANDOFF.md gate ⑹).
    /// </para>
    /// </remarks>
    private static string? PartClefWord(PartDeclarationSyntax? part)
    {
        if (part == null) return null;
        if (PartProperty(part, "clef") is string clef) return clef;
        return InstrumentPresetOf(part) is string preset
            ? InstrumentDefaults.ClefWord(InstrumentDefaults.GetDefaults(preset).Clef)
            : null;
    }

    /// <summary>
    /// The part's SOUNDING transposition in semitones, excluding the octave the clef itself
    /// carries: an explicit <c>transposition</c> property (<c>8vb</c> …) &gt; the instrument
    /// preset's default (bass = −12, piccolo = +12) &gt; the tuning's default (bass tunings =
    /// −12). RenderSpecParser.ResolvePartTransposition, on the same two tables.
    /// </summary>
    private static int PartTransposition(PartDeclarationSyntax? part)
    {
        if (part == null) return 0;
        if (PartProperty(part, "transposition") is string text
            && InstrumentDefaults.ParseTranspositionSemitones(text) is int explicitShift)
            return explicitShift;
        return InstrumentPresetOf(part) is string preset
            ? InstrumentDefaults.GetTransposition(preset)
            : Tablature.Tunings.TuningTransposition(TabTuningType(part));
    }

    /// <summary>The clef word of a part header as the model's clef, for the octave it carries
    /// (<c>treble_8</c> sounds 8vb). Unknown or absent reads as treble, which carries none.</summary>
    private static Svg.Model.ClefType ClefFromName(string? name) => name?.ToLowerInvariant() switch
    {
        "bass" => Svg.Model.ClefType.Bass,
        "alto" => Svg.Model.ClefType.Alto,
        "tenor" => Svg.Model.ClefType.Tenor,
        "treble_8" => Svg.Model.ClefType.Treble8Below,
        "bass_8" => Svg.Model.ClefType.Bass8Below,
        "treble^8" => Svg.Model.ClefType.Treble8Above,
        _ => Svg.Model.ClefType.Treble,
    };

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
