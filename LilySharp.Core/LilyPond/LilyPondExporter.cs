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
    /// The printed label each part's staff carries, by part name — the twin's
    /// <c>instrumentName</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ ASKED OF THE PAGE'S OWN READER, not re-derived. Which label a staff shows is a
    /// four-step precedence — a per-score <c>staff X "…"</c> override, then the part's inline
    /// display name, then the <c>instrument</c> preset's, then (only once two or more plain
    /// staves exist) the capitalised part name, with <c>staff ~X</c> suppressing it — and
    /// re-implementing that here would be a second spelling of a rule the twin exists to
    /// compare against. <see cref="Svg.Collector.RenderSpecParser"/> answers it.
    /// <para>
    /// ⚠️ WITHOUT THIS THE TWIN CARRIED NO NAME AT ALL, so nothing about instrument names
    /// could be measured against LilyPond — the same shape as `lysc ly` dropping lyrics,
    /// which is what left showcase/08-chorale's "verified against LilyPond" comment resting
    /// on a twin that had none.
    /// </para>
    /// <para>
    /// ⚠️ KEYED BY PART, so a score putting ONE part on two staves under two different labels
    /// keeps only the last. Two such files exist in the tree and neither is a fixture (see
    /// DuplicatePartStaffTests); a positional match would be worse, because the exporter's
    /// walk over render syntax and the spec's item list are not the same list.
    /// </para>
    /// </remarks>
    private readonly Dictionary<string, string> _instrumentNames = new(StringComparer.Ordinal);


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
    /// ⚠️ It is the written key, NOT the sounding one, and it is RIGHT to be: a
    /// <c>transpose</c> moves the key and the pitches together, and the twin says so by
    /// wrapping the whole variable in <c>\transpose c X</c> (see <see cref="TransposeTarget"/>)
    /// rather than by writing moved keys and moved pitches itself. LilyPond moves both inside
    /// the wrapper, so everything here stays the source's own spelling — which is what a
    /// transpiler owes its reader. ⚠️ This used to read "this exporter writes neither — a
    /// standing gate of its own, with one fixture in it"; the gate closed on 2026-08-17 and
    /// the fixtures were four, not one. ⚠️ It is NOT the <c>instrument</c> gate, which is also
    /// closed: see <see cref="PartClefWord"/>.
    /// </remarks>
    private int _keySharps;
    private KeyTonic _tonic = KeyTonic.CMajor;
    private int _homeKeySharps;
    private KeyTonic _homeTonic = KeyTonic.CMajor;
    // The home key's DECLARATION node (null = default C major): re-emitted verbatim
    // when a section boundary restores the score key, so mode/spelling come from the
    // source (see EmitSectionPlay).
    private KeySignatureSyntax? _homeKeyNode;

    // The running METER and the score meter a section boundary reverts it to — the twin of
    // the key pair above, and of the collector's per-voice _sectionResetTimeBeats snapshot.
    // Held as the WRITTEN pair, not a Fraction, so 4/4 and 2/2 stay distinct (they engrave
    // differently and \time takes the pair). The home node is re-emitted verbatim on a
    // restore so a `C` written in the source stays `C` (see ScoreHomeMeter).
    private int _timeBeats = 4;
    private int _timeBeatType = 4;
    private int _homeTimeBeats = 4;
    private int _homeTimeBeatType = 4;
    private TimeSignatureSyntax? _homeTimeNode;

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

    /// <summary>Whether the twin currently has <c>\improvisationOn</c> open — the
    /// LilyPond spelling of a slash-note run. Opened by the first slash, closed
    /// by the next pitched event.</summary>
    private bool _improvisationOpen;

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

    /// <summary>
    /// Section-header directives keyed by section NAME — the exporter's mirror of the
    /// collector's <c>_sectionHeaderKeys/Times/Tempos/Partials</c> registries
    /// (MeasureCollector.cs:2411-2423): any declaration of the name WITHOUT inline music
    /// registers its direct-child directives, first declaration wins per directive, and
    /// they apply to every play of that name. Keyed by name because a section reaches the
    /// form in SPLIT declarations too — <c>section A { partial 8 }</c> beside
    /// <c>part melody { section A { … } }</c> — and the played declaration is not the one
    /// holding the header. Reading the header off the chosen declaration alone
    /// (SectionHeaderMusic) lost that pickup: the twin of
    /// scratch/ベースタブLy/blogger2.lys carried no <c>\partial</c> at all (第99 handoff ③).
    /// </summary>
    private Dictionary<string, List<SyntaxNode>> _sectionHeaders = new(StringComparer.Ordinal);

    /// <summary>Sections standing in for the single-part shorthand, so
    /// <see cref="ContainerMusic"/> knows to take only their LOOSE music and leave any other
    /// part's cell alone. Identity, not name: the same section can be a container here and a
    /// mere holder of somebody else's cell for the next part.</summary>
    private readonly HashSet<SyntaxNode> _looseSections = new();

    /// <summary>Diagnostics collected while exporting (e.g. constructs dropped
    /// because they are deprecated or out of scope). Not fatal.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// The <c>form</c> this twin renders, or null for the default
    /// (<see cref="LilySharp.Core.Semantics.ScoreForms.Primary"/>).
    /// </summary>
    /// <remarks>
    /// The twin writes one <c>\score</c>, so a file with several movements takes one export
    /// per movement (<c>lysc ly --score</c> / <c>--all</c>). LilyPond can hold several
    /// <c>\score</c> blocks in one file; Lily# writes one file per score instead, which is
    /// what <c>lysc svg --all</c> already does and keeps the twin comparable score for score.
    /// </remarks>
    public FormDeclarationSyntax? Form { get; init; }

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
        _homeKeyNode = ScoreHomeKey.Declaration(root);

        // …and the meter the same boundary reverts to, read the same way.
        (_homeTimeBeats, _homeTimeBeatType) = ScoreHomeMeter.Read(root);
        _homeTimeNode = ScoreHomeMeter.Declaration(root);
        _timeBeats = _homeTimeBeats;
        _timeBeatType = _homeTimeBeatType;

        CollectPhrases(root);

        EmitHeader(root);

        var parts = root.DescendantNodes<PartDeclarationSyntax>().ToList();
        var sections = root.DescendantNodes<SectionDeclarationSyntax>().ToList();
        var form = PrimaryForm(root);
        var render = root.DescendantNodes<RenderDeclarationSyntax>().FirstOrDefault();
        CollectInstrumentNames(tree);

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
                // The clef a slash note's middle-line pitch is spelled against.
                // Read from the part's own `clef` property (the same source
                // AnchorOctaveOf reads); a preset-implied or staff-level clef is
                // not seen here, which only moves which LINE the twin's slash
                // sits on - Lily#'s page pins it to the middle regardless.
                _lysClef = part != null && PartProperty(part, "clef") is { } partClefWord
                    ? Svg.Collector.MeasureCollector.ParseClefType(partClefWord.ToLowerInvariant())
                    : ClefType.Treble;
                // The RELATIVE anchor above and the ABSOLUTE one here answer different
                // questions and resolve differently: the relative one follows the clef and
                // the instrument preset, the absolute one is middle C unless the part states
                // an `octave N`. Same split the collector makes in GetPartDefaults, and
                // reading them off one value is how the twin came to be an octave out.
                _absoluteBaseOctave = InstrumentDefaults.AbsoluteBaseOctave(
                    part != null ? ExplicitPartOctave(part) : null);
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
                _partTranspose = EffectiveTranspose(root, name, render);
                EmitPartVariable(varName, music, root);
                _drumMode = false;
            }
        }
        else
        {
            // No part at all — neither declared nor named by a score: treat the whole
            // file's music stream as one voice.
            partVars["music"] = "music";
            _lysClef = ClefType.Treble;
            _partTranspose = EffectiveTranspose(root, "music", render);
            EmitPartVariable("music", TopLevelMusic(root), root);
        }

        EmitScore(render, parts, partVars);
        return _sb.ToString();
    }

    /// <summary>
    /// What a part is transposed by, asked of the readers the page asks: the part's own
    /// <c>transpose</c> option or the file-level default
    /// (<see cref="Semantics.PartTranspose.Read(SyntaxNode, string)"/>), composed under the
    /// exported score's own <c>transpose</c> the way the collector composes it
    /// (MeasureCollector.ComposeTranspose — the part's is the INNER one).
    /// </summary>
    /// <remarks>
    /// ⚠️ "The exported score" is the FIRST one, which is what this transpiler writes; a file
    /// whose second score transposes differently has never been visible in its twin, and that
    /// is a property of exporting one score rather than of this line.
    /// ⚠️ Read through the same three houses rather than re-derived, because the three
    /// spellings of <c>transpose</c> disagreeing is a defect this repository has already had:
    /// a render block's own transpose used to be counted as the file default as well, so one
    /// construct gave three answers (PartTranspose.ReadScoreDefault's remarks).
    /// </remarks>
    private static (int step, int alt, int oct)? EffectiveTranspose(
        CompilationUnitSyntax root, string partName, RenderDeclarationSyntax? render)
    {
        var scoreTranspose = render?.Transpose is { } t
            ? Semantics.PartTranspose.ReadProperty(t)
            : null;
        // A concert-pitch FILE's instrument shift is inside PartTranspose.Read; a
        // concert-pitch SCORE's shift back to sounding pitch composes on top, as the
        // collector's GetPartDefaults composes it (Semantics.ConcertPitch).
        var scoreConcert = Semantics.ConcertPitch.OutputShift(
            Semantics.ConcertPitch.ScoreIsConcert(render),
            Semantics.ConcertPitch.FindPart(root, partName));
        return Semantics.PitchTransposer.Compose(
            Semantics.PitchTransposer.NullIfIdentity(Semantics.PitchTransposer.Compose(
                Semantics.PartTranspose.Read(root, partName), scoreConcert)),
            scoreTranspose);
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
    /// spot. A movable phrase's auto-transpose is likewise reported, not guessed: it
    /// would need the pitches re-derived, and this exporter is a transpiler that copies
    /// pitch tokens verbatim. (A reference's glued interval argument was the other such
    /// case and was reported the same way, until the spelling was removed 2026-08-28.)
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
            // (A reference used to be able to carry an interval argument — Melody'(3) —
            // which this exporter could not re-derive, so it warned that the body went out
            // UNSHIFTED. The spelling was removed 2026-08-28, so there is nothing left to
            // warn about: the marks below are whole octaves, which the twin CAN express.)
            var buf = new LilyPondExporter
            { _octaveAbsolute = _octaveAbsolute, _anchorOctave = _anchorOctave };
            CarryFrameInto(buf);
            // Both sides open a FRESH frame for the body — LilyPond the nested \relative
            // written below, Lily# its EnterDefaultFrame — at the anchor of the SECTION being
            // played, moved by the reference's own marks.
            // ⚠️ "of the section", not "of the part" (2026-08-31): a section quoted `~B'` is an
            // octave up, and a phrase body inside it is part of that play. The collector says
            // the same in one line (OctaveContext.ResetToInitial reads SectionOctaveOffset),
            // and _sectionOctaveOffset is 0 for every play written without marks.
            // ⚠️ THIS ASSIGNMENT IS BOOKKEEPING, NOT THE ANSWER — measured 2026-08-31, not
            // assumed. The two frames are set to the SAME value and EmitMusicPitch writes the
            // DIFFERENCE between them, so shifting both by a constant cancels in every mark
            // the body writes: taking the section shift out of here alone leaves the .ly BYTE
            // IDENTICAL. What decides the block's octave is its \relative reference pitch at
            // the bottom of this method; the two are kept in step so the pair cannot drift,
            // and the observer sits on the reference pitch (SectionReferenceOctaveTests
            // .APhraseBodyInsideAMarkedSectionMovesWithIt asserts the twin MOVED).
            buf._lysStep = buf._lyStep = 0;
            buf._lysOctave = buf._lyOctave =
                _anchorOctave + _sectionOctaveOffset + v.OctaveOffset;
            buf.EmitMusicStream(MusicItems(body).ToList(), "");
            _warnings.AddRange(buf._warnings);
            string inner = buf._sb.ToString().Replace("\n", " ").Trim();
            // The nested \relative the reference opens is where the two frames part company
            // (the warning above says so); stop tracking rather than guess.
            _frameTracked = false;
            if (inner.Length == 0)
                return "";

            // In absolute mode there is no relative frame to reset, so an UNMARKED
            // reference is pure inlining. A marked one moves the ANCHOR the body's bare
            // letters are measured from — the collector's OctaveBase — and LilyPond spells
            // exactly that with a nested \fixed, off the same AbsoluteBaseOctave the part
            // wrapper uses. Nesting is what makes it a SHIFT rather than a re-anchor, so a
            // doubly-referenced phrase composes the way the collector's stack does.
            // ⚠️ Off that constant, so this inherits its documented gate rather than
            // opening a second one: the constant is 4 while Lily#'s absolute anchor honours
            // a part's `octave N`, and every absolute spelling in this file is written
            // against the wrapper actually emitted. A book with both would be a whole
            // octave out HERE TOO, and consistently so — which is the point of reusing it.
            // ⚠️ This branch used to warn "the body is exported UNSHIFTED" and emit the
            // body unmoved, and it was the ONLY one of the four outputs that said anything:
            // the page, the MIDI and the MusicXML all dropped the marks in silence. It was
            // right about what happened and wrong about what should — see
            // MeasureCollector.EnterDefaultFrame.
            if (_octaveAbsolute)
                return v.OctaveOffset == 0
                    ? inner
                    : $"\\fixed {AnchorPitch(_absoluteBaseOctave + v.OctaveOffset)} {{ {inner} }}";

            // The nested \relative's own reference pitch has to be the SAME anchor the body
            // was emitted against (buf._lyOctave above), section shift included — otherwise
            // LilyPond opens the block one octave from where the body's marks were computed.
            return $"\\relative {ReferencePitch(_sectionOctaveOffset + v.OctaveOffset)} {{ {inner} }}";
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
        => InstrumentDefaults.AnchorOctave(
            ExplicitPartOctave(part),
            InstrumentPresetOf(part),
            PartProperty(part, "clef") is { } clef
                ? MeasureCollector.ParseClefType(clef.ToLowerInvariant())
                : ClefType.Treble);

    /// <summary>The part's own <c>octave N</c> property, or null when it states none — the
    /// one input both anchors take from the part, read once so the relative anchor and
    /// <see cref="_absoluteBaseOctave"/> cannot disagree about what the part said.</summary>
    private static int? ExplicitPartOctave(PartDeclarationSyntax part)
        => PartProperty(part, "octave") is { } octave && int.TryParse(octave, out int n)
            ? n
            : null;

    // ---- Header ------------------------------------------------------------

    /// <summary>
    /// The twin's <c>\version</c> and <c>\header</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE <c>font</c> DIRECTIVE IS DELIBERATELY NOT WRITTEN, decided 2026-08-18 when
    /// the per-role form landed. It has a LilyPond counterpart —
    /// <c>#(define fonts (make-pango-font-tree …))</c> in <c>\paper</c>, plus
    /// <c>font-name</c> per grob — so this is a knowing omission and not one of the
    /// exporter's silent-drop holes.
    /// <para>
    /// The twin exists to be measured against: every LP-fidelity probe compares Lily#'s
    /// geometry with what LilyPond does with the same music. Writing a font tree would
    /// change the widths LilyPond itself computes, so the twin would stop being a control
    /// — and it would change them for a directive that, on the Lily# side, does not move
    /// the layout at all (the reservation stays on the bundled face; see TextFontPlan).
    /// Emitting it would therefore introduce a difference that exists only in the
    /// comparison.
    /// </para>
    /// <para>
    /// ⚠️ What that costs, stated so it is not rediscovered as a defect: a twin rendered
    /// for a score with a <c>font</c> directive shows LilyPond's default text face rather
    /// than the score's. Nothing measures typeface identity, so no probe is blind because
    /// of it — but a human comparing the two side by side will see different letterforms.
    /// </para>
    /// </remarks>
    private void EmitHeader(CompilationUnitSyntax root)
    {
        _sb.Append("\\version \"").Append(LilyPondVersion).Append("\"\n\n");

        // ⚠️ `paper { }` is NOT exported, and unlike the font omission above this one is
        // a drummap-shaped hole, not a knowing equivalence: paper DOES move Lily#'s
        // layout, so the twin of a book that writes one is laid out on different paper
        // and stops being a control. The warning is the honest state until a probe
        // needs such a twin (no tracked book writes paper{} as of 2026-08-23); the true
        // \paper variables would map 1:1, but the staff-spacing family lives on grobs
        // and contexts in LilyPond and would need \layout overrides, so half an export
        // would be worse than a named hole.
        if (root.DescendantNodes<PaperDeclarationSyntax>().Any())
            _warnings.Add(
                "paper { } is not exported — the twin is laid out on LilyPond's default "
                + "paper, so line and page breaks differ wherever the directive bit");

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

    /// <summary>
    /// The effective <c>transpose</c> for the part being written: its own option, else the
    /// file-level default, composed under the exported score's own — the same question the
    /// collector asks (MeasureCollector's PartTranspose.Read composed with ScoreTranspose),
    /// asked of the same readers so there is no second spelling of the rule.
    /// </summary>
    private (int step, int alt, int oct)? _partTranspose;

    private void EmitPartVariable(string varName, List<SyntaxNode> music, CompilationUnitSyntax root)
    {
        // ⚠️ The two modes anchor DIFFERENTLY on purpose. Absolute octave is middle C
        //   whatever the clef (OctaveContext: "clef default is deliberately NOT used here"),
        //   so \fixed is always c'; relative follows the part's own default octave.
        // A drum part has no octave to anchor at all — its notes are names.
        string wrapper = _drumMode
            ? "\\drummode"
            : _octaveAbsolute
            ? "\\fixed " + AnchorPitch(_absoluteBaseOctave)
            : "\\relative " + AnchorPitch(_anchorOctave);
        // A transpose wraps the frame rather than sitting inside it: LilyPond resolves the
        // relative octaves of the WRITTEN pitches and shifts the result, which is the order
        // Lily# uses too (the collector transposes what the octave context has resolved).
        // ⚠️ It goes on the variable, not the \score, because it is per PART.
        if (TransposeTarget() is { } target)
            wrapper = "\\transpose c " + target + " " + wrapper;
        _sb.Append(varName).Append(" = ").Append(wrapper).Append(" {\n");

        // Each part starts from Lily#'s own default duration, as the collector does
        // (MeasureCollector resets _defaultDuration to a quarter per part).
        _lastWrittenValue = "4";
        _forceNextDuration = false;
        // Each part's music variable is its own scope - a slash run cannot stay
        // open across the boundary (the next part opened with a stray
        // improvisationOff when it did).
        _improvisationOpen = false;

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

    /// <summary>
    /// <see cref="_partTranspose"/> written the way LilyPond's <c>\transpose</c> takes it:
    /// the target of an interval FROM <c>c</c>, which is how Lily# spells it too. Null when
    /// the part is not transposed.
    /// </summary>
    /// <remarks>
    /// Both languages anchor the target on a bare <c>c</c>, so the octave marks carry over
    /// unchanged and nothing here does interval arithmetic: <c>transpose bes,</c> is
    /// <c>\transpose c bes,</c>, down a major second on both sides. (The usual "Lily#'s
    /// <c>c'</c> is LilyPond's <c>c''</c>" does not apply — that is about where a written
    /// pitch LANDS, and this is a difference between two pitches.)
    /// <para>
    /// MEASURED on LilyPond 2.26.0, 2026-08-17, because "the twin drops transpose" had been
    /// filed with the spelling left open (wrap, or write the sounding pitches?). Wrapping
    /// test/transpose's twin in <c>\transpose c d</c> makes LilyPond read exactly the ten
    /// pitches <c>lysc check --pitches</c> resolves for the page — D5 E5 F#5 G5 A5 B5 C#6 D7
    /// D#8 E9 — and moves the key signature with them: the KeySignature grob's
    /// alteration-alist goes from <c>()</c> to <c>((0 . 1/2) (3 . 1/2))</c>, C major to D
    /// major, which is what test/transpose's own header claims. So the spelling was not a
    /// decision; it was LilyPond's, and one command asked it.
    /// </para>
    /// ⚠️ Wrapping a <c>\drummode</c> body is a no-op rather than a hazard — MEASURED, the
    /// same drum book with and without the wrapper renders to a byte-identical SVG — so drum
    /// parts need no special case, and neither engine moves a drum name.
    /// LILYPOND-REF: ly/music-functions-init.ly:2437-2441 transpose, a define-music-function
    ///   taking (from to music) that wraps it in TransposedMusic via ly:music-transpose with
    ///   the interval (- to from) — which is why writing <c>c</c> on the left makes the target
    ///   the whole interval, the same way Lily#'s own target is measured from c.
    /// </remarks>
    private string? TransposeTarget()
        => _partTranspose is { } t
            ? SpellPitch(t.step, t.alt) + OctaveMarks(t.oct)
            : null;

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
        // The name-keyed header registry, rebuilt per part from the SAME document-order
        // walk the collector registers from (every declaration of a name contributes;
        // the played declaration may be a different node — see the field's remarks).
        _sectionHeaders = BuildSectionHeaderRegistry(allSections);

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
                // The language already says a top-level section in a part-major file holds
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
            AppendFormItems(FormWalk.Read(form), byName, result);
        }
        else
        {
            foreach (var entry in inOrder)
            {
                // No form: sections play in declaration order and each is labelled with
                // its own name (MeasureCollector's form-less arm sets SectionLabel =
                // SectionName), with the same boundary key-restore a formed play gets.
                var headerMusic = SectionHeaderMusic(entry.Section).ToList();
                result.Add(new SectionPlayMarker(
                    Semantics.SectionLabelRule.LabelFor(entry.Section, referenceIsSilent: false,
                        displayLabel: null, sectionName: entry.Section.SectionName),
                    headerMusic.Any(h => h is KeySignatureSyntax),
                    headerMusic.Any(h => h is TimeSignatureSyntax)));
                result.AddRange(headerMusic);
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

    /// <summary>
    /// Builds the name-keyed header registry (see <see cref="_sectionHeaders"/>): the same
    /// registration the collector performs — a declaration with inline music walks its own
    /// directives as music and registers NOTHING; any other declaration of the name
    /// contributes its first direct child of each directive type, first declaration wins per
    /// directive; the list keeps the collector's application order (time, tempo, key,
    /// partial). LILYPOND-side consumer: <see cref="AppendSection"/>, once per PLAY of the
    /// name, exactly when the collector applies its registries
    /// (MeasureCollector.Form.cs:264-301).
    /// </summary>
    private static Dictionary<string, List<SyntaxNode>> BuildSectionHeaderRegistry(
        List<SectionDeclarationSyntax> allSections)
    {
        var time = new Dictionary<string, SyntaxNode>(StringComparer.Ordinal);
        var tempo = new Dictionary<string, SyntaxNode>(StringComparer.Ordinal);
        var key = new Dictionary<string, SyntaxNode>(StringComparer.Ordinal);
        var partial = new Dictionary<string, SyntaxNode>(StringComparer.Ordinal);
        foreach (var s in allSections)
        {
            if (SectionHasInlineMusic(s))
                continue;
            var nm = s.SectionName;
            if (FirstDirect<TimeSignatureSyntax>(s) is { } ht && !time.ContainsKey(nm)) time[nm] = ht;
            if (FirstDirect<TempoDeclarationSyntax>(s) is { } hp && !tempo.ContainsKey(nm)) tempo[nm] = hp;
            if (FirstDirect<KeySignatureSyntax>(s) is { } hk && !key.ContainsKey(nm)) key[nm] = hk;
            if (FirstDirect<PartialDeclarationSyntax>(s) is { } hg && !partial.ContainsKey(nm)) partial[nm] = hg;
        }

        var result = new Dictionary<string, List<SyntaxNode>>(StringComparer.Ordinal);
        void Put(Dictionary<string, SyntaxNode> source)
        {
            foreach (var kv in source)
            {
                if (!result.TryGetValue(kv.Key, out var list))
                    result[kv.Key] = list = new List<SyntaxNode>();
                list.Add(kv.Value);
            }
        }
        Put(time); Put(tempo); Put(key); Put(partial);
        return result;
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
    /// <item><c>form main { A break B }</c> — the <c>\break</c> never reached the twin, so
    /// LilyPond broke the line wherever its own spacing put it while Lily# broke it at B.</item>
    /// <item><c>form main { A |: B :| dc A "A2" }</c> — a <c>|:</c> block is ONE child, so B
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
                        Declaration(s.Name, byName), s.Silent, s.DisplayLabel, s.Name),
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
            case FormWalk.Ending { Node: var alt }:
                AppendSection(alt.SectionName.Text, byName, result,
                    markLabel: Semantics.SectionLabelRule.LabelFor(
                        Declaration(alt.SectionName.Text, byName), alt.IsSilent,
                        alt.DisplayLabel, alt.SectionName.Text),
                    octaveOffset: alt.OctaveOffset);
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
            case FormWalk.Other o:
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
    /// <summary>The declaration a form item names, or null when the form names a section
    /// the file does not declare (AppendSection returns without emitting in that case, so
    /// the label question is moot — but SectionLabelRule still has to be asked ABOUT
    /// something, and null is its documented "ordinary default").</summary>
    private static SectionDeclarationSyntax? Declaration(
        string name,
        Dictionary<string, (SectionDeclarationSyntax Section, SyntaxNode Container)> byName)
        => byName.TryGetValue(name, out var entry) ? entry.Section : null;

    private void AppendSection(
        string name,
        Dictionary<string, (SectionDeclarationSyntax Section, SyntaxNode Container)> byName,
        List<SyntaxNode> result,
        string? markLabel = null,
        int octaveOffset = 0)
    {
        if (!byName.TryGetValue(name, out var entry))
            return;
        _sectionHeaders.TryGetValue(name, out var headers);
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
        result.AddRange(ContainerMusic(entry.Container));
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

        foreach (var child in repeat.Children)
        {
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

                case FormWalk.Ending { Node: var ending } when byName.ContainsKey(ending.SectionName.Text):
                    result.Add(CreateEnding(ending, byName));
                    break;

                default:
                    AppendFormItem(child, byName, result);
                    break;
            }
        }
    }

    /// <summary>
    /// A form ending (<c>[1. D]</c>) as the inline ending node the emitter groups.
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
        FormAlternativeSyntax ending,
        Dictionary<string, (SectionDeclarationSyntax Section, SyntaxNode Container)> byName)
    {
        var items = new List<SyntaxNode>();
        // The ending's label rule mirrors MeasureCollector.Form.cs's alternative arm
        // (alt.DisplayLabel ?? name, hidden by `~`), like the outside-a-repeat
        // FormAlternative case. ⚠️ The tilde takes the LABEL and not the ending: the volta
        // green built below is emitted whatever IsSilent says, because an ending with no
        // bracket is spelled by leaving the `[` out. See the note on that case.
        AppendSection(ending.SectionName.Text, byName, items,
            markLabel: Semantics.SectionLabelRule.LabelFor(
                Declaration(ending.SectionName.Text, byName), ending.IsSilent,
                ending.DisplayLabel, ending.SectionName.Text),
            octaveOffset: ending.OctaveOffset);

        var green = new InternalSyntax.InlineVoltaGreen(
            new InternalSyntax.SyntaxToken(SyntaxKind.OpenBracket, "["),
            new InternalSyntax.SyntaxToken(SyntaxKind.IntegerLiteral, ending.Number.Text),
            ending.Separator is { } sep ? new InternalSyntax.SyntaxToken(sep.Kind, sep.Text) : null,
            ending.EndNumber is { } end ? new InternalSyntax.SyntaxToken(end.Kind, end.Text) : null,
            new InternalSyntax.SyntaxToken(SyntaxKind.Dot, "."),
            [.. items.Select(n => n.Green)],
            ending.IsClosed ? new InternalSyntax.SyntaxToken(SyntaxKind.CloseBracket, "]") : null);

        return new InlineVoltaSyntax(green, null, ending.Position);
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

            // Once the ':|' has been seen the scan keeps running ONLY to pick up trailing
            // '[2. …]' endings. Anything else ends this repeat — in particular a second
            // '|:', which starts the NEXT repeat and must not be read as nesting. This
            // test has to come before the nesting arm below: while it sat after it,
            // '|: A :| |: B :|' re-entered as depth++ and emitted one repeat with an
            // empty '\repeat volta 2 { }' inside it, leaving B outside every repeat and
            // closed by a bare '\bar ":|."' — the page, MIDI and MusicXML all read the
            // same source as two repeats (measured 2026-08-15: 8 repeat dots / 16 noteOn /
            // two forward+backward pairs), so the twin was alone in disagreeing.
            if (closed)
            {
                if (it is InlineVoltaSyntax trailing) { alternatives.Add(trailing); continue; }
                if (it is BreakSyntax) { /* absorb a break right after :| */ continue; }
                break;
            }

            if (it is BarlineSyntax { BarToken.Kind: SyntaxKind.RepeatStartBar } && alternatives.Count == 0)
            {
                depth++; common.Add(it); continue;
            }
            if (it is BarlineSyntax rb && rb.BarToken.Kind is SyntaxKind.RepeatEndBar or SyntaxKind.RepeatBothBar)
            {
                if (depth > 0) { depth--; common.Add(it); continue; }
                repeatCount = rb.HasExplicitRepeatCount ? rb.RepeatCount : repeatCount;
                closed = true;
                // ':|:' both closes this repeat and opens the next one, so hand the token
                // back to the caller (EmitMusicStream opens a repeat on RepeatBothBar too)
                // instead of consuming it. A ':|' only closes, so keep scanning for the
                // trailing endings that may follow it.
                if (rb.BarToken.Kind == SyntaxKind.RepeatBothBar) break;
                continue;
            }
            if (it is InlineVoltaSyntax v)
            {
                alternatives.Add(v);
                continue;
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
        NoteSyntax n => CloseImprovisation() + EmitNote(n),
        DrumNoteSyntax dn => CloseImprovisation() + EmitDrumNote(dn),
        RestSyntax r => EmitRest(r),
        ChordSyntax c => CloseImprovisation() + EmitChord(c),
        ChordRepetitionSyntax q => EmitChordRepetition(q),
        SlashNoteSyntax sl => EmitSlashNote(sl),
        BareDurationSyntax bd => EmitBareDuration(bd),
        BarlineSyntax b => EmitBarline(b),
        BreakSyntax br => br.Directive switch
        {
            BreakKind.NoLine => "\\noBreak",
            BreakKind.Page => "\\pageBreak",
            BreakKind.NoPage => "\\noPageBreak",
            _ => "\\break",
        },
        TieSyntax => "~",
        SlurSyntax s => s.IsOpen ? "(" : ")",
        BeamMarkerSyntax bm => bm.IsStart ? "[" : "]",
        DynamicSyntax d => "\\" + d.DynamicToken.Text,
        KeySignatureSyntax k => EmitKey(k),
        TimeSignatureSyntax ts => EmitTime(ts),
        TempoDeclarationSyntax t => EmitTempo(t),
        ClefDeclarationSyntax cl => EmitClef(cl),
        PartialDeclarationSyntax p => EmitPartial(p),
        TupletExpressionSyntax tup => EmitTuplet(tup),
        ParallelExpressionSyntax par => EmitParallel(par),
        GraceExpressionSyntax g => EmitGrace(g),
        CueExpressionSyntax cue => EmitCue(cue),
        RepeatExpressionSyntax rep => EmitRepeat(rep),
        MusicMarkSyntax mk => EmitMark(mk),
        // The section-play sentinel is matched by its GREEN: a marker inside a rebuilt
        // volta ending comes back as a GenericSyntaxNode red, and only the green (which
        // carries the payload) survives that rebuild.
        { Green: SectionPlayGreen sp } => EmitSectionPlay(sp),
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
    /// A chord repetition passes through verbatim — LilyPond understands <c>q</c>,
    /// and BOTH engines expand it after relative resolution (LP:
    /// toplevel-music-functions; Lily#: the collector's shared resolver), so the
    /// octave frames are untouched: a <c>q</c> has no pitches when \relative runs.
    /// The duration takes the normal carry (both parsers read it as the running
    /// default), and the repetition's own post-events ride along.
    /// </summary>
    private string EmitChordRepetition(ChordRepetitionSyntax q)
    {
        var (prefix, suffix) = SplitAttachments(q.Articulations);
        string trem = q.Tremolo is { } t ? t.Text : "";
        string body = prefix + "q" + EmitEventDuration(q.Duration) + trem + suffix;

        // ⚠️ Lily#'s q takes octave marks and LilyPond's does not, so a displaced q
        // has to be SAID a different way: the chord is written out at its new octave.
        //
        // ⚠️⚠️ \transpose c c' { q } was tried first and is WRONG — it compiles and
        // changes nothing. LilyPond expands chord repetitions in
        // toplevel-music-functions, AFTER \transpose has been applied, so the wrapper
        // moves an empty placeholder and the expansion then fills in the original's
        // untransposed pitches. Measured against 2.26.0: all eight chords came out at
        // the written octave. Writing the chord out is the only spelling that holds.
        //
        // ⚠️⚠️ NOT DONE YET, and deliberately loud rather than quietly wrong. Writing
        // the chord out means re-entering EmitChord at the q's position, and that
        // function resolves its octave against the RUNNING relative frame, while a q
        // copies the original's ABSOLUTE pitches and is transparent to that frame. In
        // `octave absolute` the two agree; in relative mode they need not. Until that
        // is worked out and measured, the twin says what it cannot express instead of
        // shipping a chord in the wrong octave.
        int displacement = Music.ChordRepetitions.DisplacementOf(q);
        if (displacement != 0)
            _warnings.Add(
                "a displaced chord repetition (q" + OctaveMarks(displacement) + ") has no LilyPond "
                + "spelling — LilyPond's q takes no octave marks, and \\transpose does not reach it "
                + "because chord repetitions expand after transposition. The twin writes a plain q, "
                + "so it sounds an octave away from the Lily# score; write the chord out by hand.");
        return body;
    }

    /// <summary>The written pitch that sits on the MIDDLE staff line of a clef -
    /// where Lily# draws every slash note. Step index per
    /// <see cref="Semantics.RelativeOctave.StepIndex"/>, octave in the c'=4
    /// convention.</summary>
    private static (int Step, int Octave) MiddleLinePitch(ClefType clef) => clef switch
    {
        ClefType.Bass => (1, 3),   // d
        ClefType.Alto => (0, 4),   // c'
        ClefType.Tenor => (5, 3),  // a
        _ => (6, 4),               // b' - treble, treble_8, and anything unmapped
    };

    /// <summary>
    /// A slash note. LilyPond has no pitchless slash token, so the twin spells it
    /// the way LilyPond users do: <c>\improvisationOn</c> (slash heads, no
    /// accidentals - ly/property-init.ly) around a pitch on the clef's middle
    /// line. The pitch moves LILYPOND's relative frame and not Lily#'s (a slash
    /// has no pitch), so only <see cref="_lyStep"/>/<see cref="_lyOctave"/>
    /// advance; <see cref="EmitMusicPitch"/> already compensates the next real
    /// pitch for diverged frames.
    /// </summary>
    private string EmitSlashNote(SlashNoteSyntax slash)
    {
        var (prefix, suffix) = SplitAttachments(slash.Articulations);
        string trem = slash.Tremolo is { } t ? t.Text : "";
        var (step, octave) = MiddleLinePitch(_lysClef);
        string pitchText;
        if (_octaveAbsolute)
        {
            pitchText = StepName(step) + OctaveMarks(octave - _absoluteBaseOctave);
        }
        else
        {
            int marks = octave - Semantics.RelativeOctave.Resolve(_lyStep, _lyOctave, step, 0);
            pitchText = StepName(step) + OctaveMarks(marks);
            _lyStep = step;
            _lyOctave = octave;
        }
        string on = _improvisationOpen ? "" : "\\improvisationOn ";
        _improvisationOpen = true;
        return on + prefix + pitchText + EmitEventDuration(slash.Duration) + trem + suffix;
    }

    /// <summary>Closes an open slash run before a pitched event; empty otherwise.</summary>
    private string CloseImprovisation()
    {
        if (!_improvisationOpen) return "";
        _improvisationOpen = false;
        return "\\improvisationOff ";
    }

    private static string StepName(int step) => "cdefgab"[step].ToString();

    /// <summary>
    /// A bare duration passes through verbatim - LilyPond 2.20+ reads an isolated
    /// duration as the previous note or chord again (LILYPOND-REF: lily/parser.yy
    /// music_embedded), which is Lily#'s reading too; both engines fill the pitch
    /// after relative resolution, so the octave frames are untouched. The number
    /// takes the normal written-duration carry, and the repetition's own
    /// post-events ride along.
    /// </summary>
    private string EmitBareDuration(BareDurationSyntax bare)
    {
        var (prefix, suffix) = SplitAttachments(bare.Articulations);
        string trem = bare.Tremolo is { } t ? t.Text : "";
        return prefix + EmitEventDuration(bare.Duration) + trem + suffix;
    }

    /// <summary>
    /// A pitch in the music stream: the source's own token, and the octave frames advanced
    /// past it. See <see cref="_lysStep"/> for why the marks are not always the source's.
    /// </summary>
    private string EmitMusicPitch(PitchSyntax p)
    {
        // \fixed has no frame: every mark is an absolute offset from the wrapper's c' —
        // plus whatever octave THIS section's reference asked for (see
        // _sectionOctaveOffset; zero for every play written without marks).
        if (_octaveAbsolute)
            return p.PitchToken.Text + OctaveMarks(p.OctaveOffset + _sectionOctaveOffset);

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
    /// Hands a nested body's exporter the state a body is emitted against — the two octave
    /// frames, the running key, and the phrase table — so a body written into a temporary
    /// buffer sees what the stream around it sees.
    /// </summary>
    private void CarryFrameInto(LilyPondExporter buf)
    {
        // The phrase table and the set of references currently being expanded. ONE home for
        // this, because two of the six nested-exporter sites used to set them for themselves
        // and the other four therefore did not have them: a reference inside a tuplet, a
        // grace, a cue or a repeat resolved against an EMPTY table and exported as nothing,
        // under a warning that called the phrase "referenced but not declared" while the
        // file declared it. MEASURED 2026-08-17: samples/canon-in-d.lys, whose header
        // advertises a ground "written ONCE and cycled 13 times", emitted
        // `\repeat unfold 13 {  }` — 53 bars of continuo on the page against 1 in the twin,
        // so every LilyPond comparison taken through that book compared different music. The
        // hole had been recorded here as unobservable on the strength of "0 of 300 books";
        // the tree has 566 and the one book that writes the spelling was in the other 266.
        // ⚠️ _activePhrases is SHARED, not copied: recursion has to be caught through a
        // container as well (`phrase A { tuplet 3/2 { A } }`), and a copy would let the inner
        // reference open the phrase again and expand forever.
        buf._phrases = _phrases;
        buf._activePhrases = _activePhrases;
        // The ABSOLUTE anchor belongs with the two relative frames below — it is what a pitch
        // resolves against in the other octave mode. All six nested-exporter sites set
        // _octaveAbsolute and _anchorOctave in their initializers and then call this, so it
        // rides here rather than being copied six times.
        // ⚠️ LILYSHARP-OWN: correct by construction. A degree chord's two uses of this value
        // CANCEL — the anchor is base + rootOffset and the written mark is octave − base — so
        // no nesting of one can see it. The use that does NOT cancel is the nested \fixed a
        // marked phrase reference emits, which is exactly what a nested body can now do; the
        // observer is AMarkedReference_MovesTheAnchor_WithANestedFixed reached through a
        // container, and the value stops being unobserved with the line above.
        buf._absoluteBaseOctave = _absoluteBaseOctave;
        // …and the section reference's absolute shift with it, for the same reason: a nested
        // body written inside a `~B'` play sounds where the play sounds.
        buf._sectionOctaveOffset = _sectionOctaveOffset;
        buf._drumMode = _drumMode;
        buf._improvisationOpen = _improvisationOpen;
        buf._lysClef = _lysClef;
        buf._lysStep = _lysStep;
        buf._lysOctave = _lysOctave;
        buf._lyStep = _lyStep;
        buf._lyOctave = _lyOctave;
        buf._frameTracked = _frameTracked;
        buf._keySharps = _keySharps;
        buf._tonic = _tonic;
        buf._homeKeySharps = _homeKeySharps;
        buf._homeTonic = _homeTonic;
        buf._timeBeats = _timeBeats;
        buf._timeBeatType = _timeBeatType;
        buf._homeTimeBeats = _homeTimeBeats;
        buf._homeTimeBeatType = _homeTimeBeatType;
        buf._homeTimeNode = _homeTimeNode;
    }

    /// <summary>
    /// Takes the state back out of a body that is plain sequential music on BOTH sides (a
    /// tuplet, a repeat) — the stream continues where the body left off. Bodies whose frame
    /// the two engines hand over differently (a grace, a voice span, a phrase reference) do
    /// not call this; they clear <see cref="_frameTracked"/> instead.
    /// </summary>
    private void CarryFrameBack(LilyPondExporter buf)
    {
        _improvisationOpen = buf._improvisationOpen;
        _lysClef = buf._lysClef;
        _lysStep = buf._lysStep;
        _lysOctave = buf._lysOctave;
        _lyStep = buf._lyStep;
        _lyOctave = buf._lyOctave;
        _frameTracked = buf._frameTracked;
        _keySharps = buf._keySharps;
        _tonic = buf._tonic;
        _timeBeats = buf._timeBeats;
        _timeBeatType = buf._timeBeatType;
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
                ? _absoluteBaseOctave + root.OctaveOffset + off
                : RelativeOctave.Resolve(_lysStep, _lysOctave, anchorStep, 0) + off;
        }
        else if (hasDegrees)
        {
            // Omitted root (<1 3 5>): degree 1 is the KEY'S TONIC, anchored in the frame as a
            // written root would be. A custom/atonal key has no tonic, so C — the collector's
            // own fallback.
            anchorStep = _tonic.Valid ? _tonic.Step : 0;
            anchorOctave = _octaveAbsolute
                ? _absoluteBaseOctave + off
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
            else if (_octaveAbsolute)
            {
                // Absolute: every pitch stands on its own, so every one carries the shift —
                // the chord-level marks, and the section reference's own (_sectionOctaveOffset).
                sb.Append(p.PitchToken.Text)
                  .Append(OctaveMarks(p.OctaveOffset + _sectionOctaveOffset))
                  .Append(marks);
            }
            else
            {
                // Relative: Lily# STACKS the member on the ROOT — same octave as the root,
                // bumped when its letter is below the root's, plus its own marks — while
                // LilyPond CHAINS member to member and takes the nearest. So the source's
                // marks are not the twin's: they are recomputed against the chain, exactly
                // as the degrees below are. LILYPOND-REF for the chain: music-sequence.cc
                // :142-160; Lily#'s rule: MeasureCollector.ItemFactory CreateChordItem
                // (`firstOctave + (step >= rootStepForStack ? 0 : 1) + pitch.OctaveOffset`),
                // which the collector's own comment calls a deliberate divergence.
                // ⚠️ Copying them verbatim made `<a c g>` a DIFFERENT CHORD in the twin
                // (Lily# A3 C4 G4, LilyPond A3 C4 G3) — measured on test/tab-beam-slope,
                // whose notation beam was the last one differing after gate ⑹.
                // ⚠️ The LETTER is still the source's, verbatim (PitchToken.Text carries the
                // accidental and any quarter tone) — only the octave MARKS are recomputed.
                int step = RelativeOctave.StepIndex(p.PitchName[0]);
                int want = anchorOctave + (step >= anchorStep ? 0 : 1) + p.OctaveOffset;
                sb.Append(p.PitchToken.Text)
                  .Append(OctaveMarks(want - RelativeOctave.Resolve(chainStep, chainOctave, step, 0)));
                chainStep = step;
                chainOctave = want;
            }

            // Member-level post-events. Lily# renders these per MEMBER
            // (ChordNoteInfo.HasLaissezVibrer/HasRepeatTie, NoteInChord.Fingering), so a
            // twin that dropped one would show fewer ties — or fewer digits — than its
            // source. The half-ties take the neutral `-`, the member form the regression
            // books themselves write (repeat-tie-chords.ly, laissez-vibrer-chords.ly);
            // ^/_ comes back from MapArticulation already prefixed.
            // ⚠️ THE FINGERING WAS DROPPED IN SILENCE UNTIL 2026-08-10, and it is the SAME
            // defect session 96 closed one level up: <c@finger(1) e@finger(3) g@finger(5)>
            // exported as a bare <c e g>, a twin that COMPILES and is DIFFERENT MUSIC. It
            // was caught building audit/lp-geometry/probes/chord-fingering.ly, whose two
            // books measure exactly the digits that went missing. The note-level hole was
            // caught by the exporter's WARNING; this one had none to raise, because a
            // member articulation the loop did not recognise simply fell out of the `if`.
            // ⇒ every unrecognised member node now warns, so the next hole in this family
            // is visible the way the last one was.
            // LILYPOND-REF: lily/parser.yy:3165-3166 chord_body_element — a chord member takes
            //   post-events (`<g-1 b-3 d'-5>`), the same spelling as a note's.
            foreach (var art in p.Articulations)
                switch (art)
                {
                    case ArticulationSyntax { Type: ArticulationType.None } ma
                        when ma.NameToken.Text.Equals("laissezvibrer", StringComparison.OrdinalIgnoreCase)
                             || ma.NameToken.Text.Equals("repeattie", StringComparison.OrdinalIgnoreCase):
                        string ev = MapArticulation(ma);
                        if (ev[0] == '\\')
                            sb.Append('-');
                        sb.Append(ev);
                        break;
                    case MusicMarkSyntax mk when Fingering(mk) is { } fg:
                        sb.Append(fg);
                        break;
                    default:
                        _warnings.Add(
                            $"chord member {p.PitchName}: {art.GetType().Name} dropped (out of scope)");
                        break;
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
                ? octave - _absoluteBaseOctave + _sectionOctaveOffset
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
    /// Read off the part exactly as the collector reads it
    /// (<c>InstrumentDefaults.AbsoluteBaseOctave</c> = the explicit <c>octave N</c> property,
    /// or 4 — LilyPond's fixed <c>c'</c> — when the part declares none). The CLEF's default is
    /// deliberately not used: absolute octave is middle C whatever the clef, which is what
    /// <c>OctaveContext.OctaveBase</c> says too.
    /// <para>
    /// ⚠️ This was <c>const int = 4</c> until 2026-08-16, and its remark called the mismatch
    /// "an existing gate, not a new one". It was not a gate — no entry in the gate list
    /// excluded those books, so they were compared against LilyPond as different music:
    /// <c>test/octave-base.lys</c> declares <c>octave 3</c>, its own header says bare
    /// <c>c</c> is C3, the page draws C3, and the twin said <c>\fixed c'</c> = C4. A whole
    /// octave, in the one direction nothing was watching.
    /// </para>
    /// <para>
    /// It has to be ONE value for the whole part: the body's pitches are written with the
    /// SOURCE's own marks (see <c>EmitMusicPitch</c>'s absolute arm), so the wrapper is the
    /// only thing that decides what they sound, and the degree spellings below measure their
    /// marks from the same anchor. Move one without the others and the twin becomes wrong in
    /// a way that looks right.
    /// ⚠️ "The source's own marks" gained ONE addend on 2026-08-31 — a marked section
    /// reference's <see cref="_sectionOctaveOffset"/> — and it is deliberately NOT this
    /// value: see that field's remarks for why shifting the base instead would cancel.
    /// </para>
    /// </remarks>
    private int _absoluteBaseOctave = 4;

    /// <summary>
    /// ABSOLUTE mode's half of a marked section reference (<c>~B'</c>): the octaves to add
    /// to every pitch this play writes, reset at each section boundary by
    /// <c>EmitSectionPlay</c>.
    /// </summary>
    /// <remarks>
    /// It is a separate running value rather than a shift of <see cref="_absoluteBaseOctave"/>
    /// because the two are read in opposite directions and would CANCEL: the base is what the
    /// part's <c>\fixed</c> wrapper is written from and what a degree's marks are measured
    /// AGAINST (<c>octave − base</c>), so moving it up moves the anchor and the subtraction by
    /// the same amount and nothing lands anywhere new. What the twin has to say is "one octave
    /// higher than the source wrote", and that is an addend on the WRITTEN marks.
    /// ⚠️ A slash note deliberately does not read it: it stands on the clef's middle line in
    /// both engines and carries no pitch to shift (MeasureCollector does the same). MEASURED
    /// 2026-08-31, not assumed — <c>section B { /4 4 4 4 | }</c> played as <c>~B</c> and as
    /// <c>~B'</c> gives the same page and the same <c>b,4</c> here; the observer is
    /// <c>SectionReferenceOctaveTests.ASlashNoteDoesNotMove_ItStandsOnTheClefsMiddleLine</c>.
    /// </remarks>
    private int _sectionOctaveOffset;

    // A note's attachments split into those that must precede the note (a
    // rehearsal \mark, a \deadNote prefix, a forced stem direction) and those
    // that trail it (string numbers, ties, dynamics, articulation scripts).
    private (string Prefix, string Suffix) SplitAttachments(IEnumerable<SyntaxNode> arts)
    {
        var prefix = new StringBuilder();
        var suffix = new StringBuilder();
        foreach (var a in arts)
        {
            switch (a)
            {
                // ⚠️ THE ONE MARK THAT TRAILS ITS NOTE. `\mark` is standalone music written
                // BEFORE the note it stands over, which is why marks default to the prefix;
                // `\nonArpeggiato` is a POST-EVENT (scm/define-music-types.scm:436-441 gives
                // its syntax as `note-\nonArpeggiato`), so written in the prefix it would be
                // an unattached post-event that LilyPond drops with a warning — a twin that
                // COMPILES but engraves different music, which is the one failure mode a twin
                // generator must not have.
                case MusicMarkSyntax mk when NonArpeggiato(mk) is { } na:
                    suffix.Append(na);
                    break;
                // …and the second one. `-2` is a post-event exactly like \nonArpeggiato, for
                // the same reason and with the same consequence if it were written first.
                case MusicMarkSyntax mk when Fingering(mk) is { } fg:
                    suffix.Append(fg);
                    break;
                case MusicMarkSyntax mk:
                    string m = EmitMark(mk);
                    if (m.Length > 0) prefix.Append(m).Append(' ');
                    break;
                case ArticulationSyntax art when IsDeadNote(art):
                    prefix.Append("\\deadNote ");
                    break;
                // `a4@rest` is LilyPond's own `a4\rest` — a post-event, so it goes in the
                // suffix right where the reader expects it. Without this the twin wrote
                // the NOTE, which is not the same music: LilyPond would engrave a head
                // where the book prints a rest.
                // LILYPOND-REF: ly/music-functions-init.ly — \rest as a post-event.
                case ArticulationSyntax art when IsPitchedRest(art):
                    suffix.Append("\\rest");
                    break;
                case ArticulationSyntax art when StemDirectionOverride(art) is { } up:
                    prefix.Append(up ? "\\once \\override Stem.direction = #UP "
                                     : "\\once \\override Stem.direction = #DOWN ");
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

    // One house for the spelling (Semantics.PitchedRest), because there are four readers of
    // it and two of them did not have one: MusicXML exported a sounding note and MIDI played
    // it. This was the only output that got it right.
    // ⚠️ Moving here TIGHTENS this reader — it used to match any articulation spelled "rest"
    // and now requires ArticulationType.None, which is what the collector has always
    // required. That is a move toward agreement rather than a change of behaviour, and it is
    // measured: the twin of all 566 tracked books is byte-identical across it.
    private static bool IsPitchedRest(ArticulationSyntax a) => Semantics.PitchedRest.IsMarker(a);

    /// <summary>
    /// <c>@stemUp</c> / <c>@stemDown</c> as a nullable direction, or null for anything else.
    /// </summary>
    /// <remarks>
    /// Written as <c>\once \override Stem.direction</c> rather than <c>\stemUp</c>, because
    /// Lily#'s annotation belongs to ONE note while LilyPond's command is a context setting
    /// that runs until <c>\stemNeutral</c>. <c>\once</c> is the same scope — one timestep —
    /// and needs no closing command, so a run of annotated and un-annotated notes comes out
    /// note for note.
    /// <para>
    /// LILYPOND-REF: ly/property-init.ly <c>stemUp</c> — <c>\override Stem.direction = #UP</c>;
    /// lily/beam.cc:898-903 Beam::get_default_dir is what then reads it, which is why the
    /// twin needs it at all: without this the beamed books of
    /// audit/lp-geometry/probes/tie-direction.ly would engrave a DOWN beam in LilyPond and
    /// an UP one in Lily#.
    /// </para>
    /// </remarks>
    private static bool? StemDirectionOverride(ArticulationSyntax a)
        => a.NameToken.Text.ToLowerInvariant() switch
        {
            "stemup" => true,
            "stemdown" => false,
            _ => null,
        };

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

        var parts = new List<string>(3);
        // ⚠️ THE METER REVERTS HERE TOO, and this arm is the twin of the key one below.
        // A section that states no `time` of its own opens at the SCORE meter, so a
        // mid-section change in a PRIOR section (or in an earlier play of this one) must
        // not leak across — MeasureCollector.ProcessSectionPrologue reverts it against the
        // per-voice snapshot and the page draws the restored signature. Until 2026-08-31
        // this carrier answered only the key question, so `section A { … time 3/4 … }
        // section B { c'4 d e f | }` handed LilyPond a 3/4 bar holding four quarters.
        if (!sp.HasHeaderTime
            && (_timeBeats != _homeTimeBeats || _timeBeatType != _homeTimeBeatType))
        {
            if (_homeTimeNode != null)
            {
                parts.Add(EmitTime(_homeTimeNode)); // EmitTime advances the running meter
            }
            else
            {
                parts.Add("\\time 4/4");
                _timeBeats = 4;
                _timeBeatType = 4;
            }
        }
        if (!sp.HasHeaderKey && (_keySharps != _homeKeySharps || _tonic != _homeTonic))
        {
            if (_homeKeyNode != null)
            {
                parts.Add(EmitKey(_homeKeyNode)); // EmitKey advances _keySharps/_tonic
            }
            else
            {
                parts.Add("\\key c \\major");
                _keySharps = 0;
                _tonic = KeyTonic.CMajor;
            }
        }
        if (sp.MarkLabel is { Length: > 0 } label)
            parts.Add("\\mark \\markup \\box \"" + Escape(label) + "\"");
        return string.Join(" ", parts);
    }

    /// <summary><c>@mark("Intro")</c> → LilyPond's boxed rehearsal mark.</summary>
    /// <remarks>
    /// <para>
    /// The label is read from the annotation's argument by
    /// <see cref="Semantics.AnnotationValues.Rehearsal"/>, not sliced out of the dotted
    /// name here — this was the FOURTH copy of that slice, and it stripped its quotes
    /// with <c>Trim('"')</c> where the collector strips one balanced pair
    /// (docs/VALUE_SITE_AUDIT.md §9.5.3 ⑵).
    /// </para>
    /// <para>
    /// ⚠️ <b>A behaviour change, declared</b> — the same shape as <c>@finger("3")</c>
    /// and <c>@frame(zzz)</c> before it: the twin wrote the label UNQUOTED, and
    /// <c>\box</c> takes ONE markup argument, so a label with a space said different
    /// music than Lily# draws. Measured on LilyPond 2.26.0: <c>\box a b</c> boxes only
    /// <c>a</c> and prints <c>b</c> outside the box (box width 1.9331), while
    /// <c>\box "a b"</c> boxes the whole label (4.0159) — which is what Lily# draws.
    /// Quoting is a no-op for every label a book writes: <c>\box A</c> and
    /// <c>\box "A"</c> render byte-identical SVG, as do <c>\box D.S.</c> and
    /// <c>\box "D.S."</c> (both measured on 2.26.0), and the two <c>@mark(</c> sites in
    /// the corpus and fixtures are <c>@mark("A")</c> and <c>@mark("B")</c>.
    /// </para>
    /// LILYPOND-REF: ly/music-functions-init.ly:1159-1171 mark = define-music-function (label)
    ///   — the label of <c>\mark</c>, which becomes a RehearsalMarkEvent (or an
    ///   AdHocMarkEvent when it is a markup rather than a number).
    /// LILYPOND-REF: scm/define-markup-commands.scm:1049-1053 — define-markup-command
    ///   (box layout props arg) declares <c>(markup?)</c>: ONE markup, which is why an
    ///   unquoted two-word label boxes only its first word.
    /// </remarks>
    private string EmitMark(MusicMarkSyntax mk)
    {
        if (Semantics.AnnotationValues.Rehearsal(mk, out _) is { } label)
            return $"\\mark \\markup {{ \\box \"{Escape(label)}\" }}";
        string name = mk.MarkName;
        if (NonArpeggiato(mk) is { } na)
            return na;
        if (Fingering(mk) is { } fg)
            return fg;
        // Spelt as WRITTEN: MarkName steps over the '!' of a terminator, so a bare
        // "@rit dropped" for a '@!rit' would name a mark the reader did not write.
        _warnings.Add($"@{(mk.IsSpanEnd ? "!" : "")}{name} dropped (out of scope)");
        return "";
    }

    /// <summary>
    /// <c>@arpeggio(bracket)</c> as LilyPond's post-event for it, or null for any other mark.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS NOT <c>\arpeggioBracket</c>, and that is the whole reason it can be written
    /// at all. <c>\arpeggioBracket</c> (ly/property-init.ly:99-108) is a pair of OVERRIDES that
    /// change what an ordinary <c>\arpeggio</c> draws, so a twin would need a
    /// <c>\once \override</c> pair in the PREFIX and an <c>\arpeggio</c> in the SUFFIX — and
    /// every other mark here contributes to one side only, which is why this annotation was
    /// dropped as unwritable until 2026-08-03.
    /// <para>
    /// LilyPond's spelling for the THING rather than for the appearance needs no prefix at all:
    /// LILYPOND-REF: ly/property-init.ly:69 — <c>nonArpeggiato = #(make-music
    ///   'NonArpeggiatoEvent)</c>, whose syntax (scm/define-music-types.scm:436-441) is
    ///   <c>note-\nonArpeggiato</c>, a post-event like <c>\arpeggio</c> itself.
    /// LILYPOND-REF: lily/arpeggio-engraver.cc:91-98 <c>listen_non_arpeggiato</c> — that event
    ///   sets <c>Arpeggio_type::NON_ARPEGGIATED</c>, and
    /// LILYPOND-REF: lily/arpeggio-engraver.cc:132-148 <c>process_music</c> — which then makes
    ///   a <b>ChordBracket</b> item, the grob <c>ArpeggioItem.Bracket</c> means.
    ///   <c>\arpeggioBracket</c> instead keeps an <b>Arpeggio</b> grob and re-dresses it.
    /// </para>
    /// <para>
    /// LilyPond's own docstring (ly/property-init.ly:103-104) prefers this one for exactly this
    /// case: "For a bracket designating a non-arpeggiated chord, it is better to use
    /// <c>\nonArpeggiato</c> than to use <c>\arpeggio</c> and alter the appearance."
    /// </para>
    /// </remarks>
    private static string? NonArpeggiato(MusicMarkSyntax mk)
        => Semantics.AnnotationValues.IsArpeggioBracket(mk) ? "\\nonArpeggiato" : null;

    /// <summary>
    /// <c>@finger(2)</c> as LilyPond's fingering post-event <c>-2</c>, or null for any other
    /// mark.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/parser.yy:3461-3467 fingering — an UNSIGNED after a direction sign becomes a FingeringEvent carrying `digit`
    /// <para>
    /// ⚠️ THE GRAMMAR, NOT THE ENGRAVER, and the first version of this citation got it
    /// wrong: it named <c>fingering-engraver.cc</c>, which is what CONSUMES a FingeringEvent.
    /// What this line asserts is how LilyPond SPELLS one, so the address has to be where the
    /// spelling is defined. An exporter's citations point at LilyPond's syntax; an
    /// engraver's point at its arithmetic.
    /// </para>
    /// <para>
    /// ⚠️ WHY THIS EXISTS AT ALL, AND IT IS THE FAILURE MODE THE CLASS REMARK NAMES: until
    /// 2026-08-05 (session 96) this fell through to <c>EmitMark</c>'s "out of scope" branch,
    /// so a fixture carrying a fingering exported as a bare note and the twin had NO
    /// Fingering grob — a twin that COMPILES and is DIFFERENT MUSIC. It was caught while
    /// building audit/lp-geometry/probes/notehead-ink-frame.ly, whose FNG book needed the
    /// `-2` inserted by hand; the warning is what caught it, which is why every drop here
    /// raises one.
    /// </para>
    /// ⚠️ A POST-EVENT, so it belongs in the SUFFIX beside <c>\nonArpeggiato</c> — see the
    /// remark in <see cref="SplitAttachments"/> for what putting one in the prefix costs.
    /// ⚠️ The DIRECTION is deliberately left to LilyPond (`-2`, not `^2`/`_2`): Lily#'s own
    /// engraver takes the default orientation too (fingeringOrientations '(up down), so a
    /// lone fingering goes up regardless of stem), and forcing a side here would make the
    /// twin state something the fixture did not.
    /// </remarks>
    private static string? Fingering(MusicMarkSyntax mk)
        // The SAME set the collector reads, so anything Lily# engraves reaches the twin:
        // any non-negative integer.
        // ⚠️ THIS GATE SAID 1-5 FOR ONE COMMIT, and that was a narrowing with nothing behind
        // it — it would have dropped @finger(6) from the twin while Lily# drew it, which is
        // the very defect this method exists to close, just over a smaller range. MEASURED
        // rather than argued (scratch probe on 2.26.0, dumping the Fingering grob's `text`):
        // LilyPond engraves `-0`, `-5`, `-6` AND `-12` as fingerings reading 0/5/6/12, so
        // its grammar's UNSIGNED really does take them all and there is nothing to protect
        // against here.
        // ⚠️ "The SAME set" was an ASPIRATION until 2026-08-15: this read used to slice the
        // dotted MarkName and Trim('"') it, so it alone accepted `@finger("3")` and emitted
        // a `-3` for a fingering Lily# does not draw. Both now read the argument through
        // Semantics.AnnotationValues.Finger, which is where that set lives.
        => Semantics.AnnotationValues.Finger(mk) is { } finger
            ? "-" + finger.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;

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

    private string EmitBarline(BarlineSyntax b)
    {
        // A ':|' that EmitInlineRepeat did not consume is a ONE-SIDED end-repeat, and in
        // Lily# that means "repeat from the beginning of the piece" — which `\bar ":|."`
        // does NOT say. LilyPond's `\bar` is a glyph; only `\repeat volta` repeats. So the
        // twin draws the right barline and plays the music once, and that is a twin that
        // COMPILES AND IS DIFFERENT MUSIC — the defect class this exporter's warning channel
        // exists for (see the remark on Fingering_BecomesAnAttachedPostEvent). Say so rather
        // than let it pass: wrapping the whole preceding stream in `\repeat volta 2 { … }`
        // is the fix, and it is a restructure of the emitted file, not a token swap.
        if (b.BarToken.Kind == SyntaxKind.RepeatEndBar)
            _warnings.Add(
                "a one-sided ':|' repeats from the beginning of the piece in Lily#, but "
                + "LilyPond's \\bar \":|.\" only DRAWS the barline — the twin engraves the "
                + "same page and plays the music once");

        return b.BarToken.Kind switch
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
    }

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

    /// <summary>
    /// A written meter, and the one place the RUNNING meter advances — so a section
    /// boundary can tell whether the score meter still stands (see EmitSectionPlay).
    /// </summary>
    private string EmitTime(TimeSignatureSyntax ts)
    {
        if (!ts.IsSenzaMisura)
        {
            _timeBeats = ts.Beats;
            _timeBeatType = ts.BeatType;
        }
        return TimeText(ts);
    }

    private static string TimeText(TimeSignatureSyntax ts)
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
            { _octaveAbsolute = _octaveAbsolute, _anchorOctave = _anchorOctave };
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

    // The clef the twin is reading in, so an UNCHANGED `clef` can be told from a change.
    private ClefType _lysClef = ClefType.Treble;

    /// <summary>A mid-music <c>clef</c>: written across unchanged, and the LILY# frame — not
    /// LilyPond's — reopened at the clef's own octave.</summary>
    /// <remarks>
    /// ⚠️ THE TWO FRAMES PART COMPANY HERE, deliberately. Lily# reads the notes after a clef
    /// change in that clef's register (`clef bass c,4` is a low C); LilyPond's <c>\relative</c>
    /// never looks at a clef and would carry on from the last note. Moving <see
    /// cref="_lysOctave"/> alone is exactly what makes <see cref="EmitMusicPitch"/> write the
    /// difference out as octave marks — the same machinery that already corrects a degree
    /// chord — so the twin sounds the page's music while LilyPond does nothing unusual.
    /// ⚠️ An UNCHANGED clef changes nothing: it engraves no grob and must not move the frame
    /// either (MeasureCollector.MusicWalk's clef branch, citing
    /// lily/clef-engraver.cc:139-166 inspect_clef_properties).
    /// Decided 2026-08-17, HANDOFF §3; measured on `test/clef-change`, whose twin used to
    /// hand LilyPond C5 D5 where the page prints C3 D3.
    /// </remarks>
    private string EmitClef(ClefDeclarationSyntax cl)
    {
        string text = "\\clef " + LyClefName(cl.ClefName.Text);
        var next = Svg.Collector.MeasureCollector.ParseClefType(cl.ClefName.Text.ToLowerInvariant());
        if (next != _lysClef && !_octaveAbsolute)
        {
            _lysClef = next;
            _lysOctave = InstrumentDefaults.GetDefaultOctave(next);
        }
        else _lysClef = next;
        return text;
    }

    /// <summary>
    /// Emits a cue region as LilyPond's own <c>\new CueVoice { … }</c>, with
    /// <c>\cueClef</c> / <c>\cueClefUnset</c> around it when the region names a clef.
    /// </summary>
    /// <remarks>
    /// This is the 1:1 that made a cue twin possible at all: LilyPond has no per-note cue,
    /// so `lysc ly` used to drop `@cue` and emit a book with no cue in it. The region maps
    /// straight across with nothing to infer. ⚠️ BOTH clefs are written — MEASURED
    /// (audit/lp-geometry/probes/cue-span.ly, book D-NOUNSET) LilyPond leaks the cue clef
    /// into the rest of the staff without the unset.
    /// LILYPOND-REF: ly/engraver-init.ly CueVoice; ly/music-functions-init.ly cueClef /
    ///   cueClefUnset.
    /// </remarks>
    private string EmitCue(CueExpressionSyntax cue)
    {
        var buf = new LilyPondExporter
        { _octaveAbsolute = _octaveAbsolute, _anchorOctave = _anchorOctave };
        CarryFrameInto(buf);
        // ⚠️ A cue clef reopens the LILY# frame at both edges, unconditionally — the page
        // does it whether or not the cue clef differs from the staff's
        // (MeasureCollector.MusicWalk.ProcessCueRegion). LilyPond's \cueClef does not touch
        // its own \relative chain, so only buf._lysOctave moves and EmitMusicPitch writes
        // the difference out. `audit/lp-regression/lys/cue-clef-manually` documents the rule
        // in its own margin and compensates for it by hand.
        if (cue.ClefKeyword is { } cueClefTok && !_octaveAbsolute)
        {
            buf._lysClef = Svg.Collector.MeasureCollector.ParseClefType(
                cueClefTok.Text.ToLowerInvariant());
            buf._lysOctave = InstrumentDefaults.GetDefaultOctave(buf._lysClef);
        }
        buf.EmitMusicStream(MusicItems(cue.Body).ToList(), "");
        // The body is written once and read once by the relative pass on both sides, so its
        // frame carries out like a tuplet's or a repeat's.
        CarryFrameBack(buf);
        if (cue.ClefKeyword != null && !_octaveAbsolute)
            _lysOctave = InstrumentDefaults.GetDefaultOctave(_lysClef); // the staff's own clef is back
        _warnings.AddRange(buf._warnings);
        string body = buf._sb.ToString().Replace("\n", " ").Trim();
        string region = $"\\new CueVoice {{ {body} }}";
        return cue.ClefKeyword is { } clef
            ? $"\\cueClef {LyClefName(clef.Text)} {region} \\cueClefUnset"
            : region;
    }

    /// <summary>
    /// A clef name as LilyPond's <c>\clef</c> / <c>\cueClef</c> take it: a QUOTED string.
    /// </summary>
    /// <remarks>
    /// ⚠️ The quotes are not decoration. MEASURED on LilyPond 2.26.0, 2026-08-15: written
    /// bare, <c>\clef treble_8</c> is read as <c>\clef treble</c> followed by <c>_8</c> — a
    /// fingering — so LilyPond reports "Unattached FingeringEvent", engraves an ORDINARY
    /// treble clef and prints a stray glyph under the staff. The three books differ:
    /// bare 5643 bytes, quoted 6442 (the real octave-down clef), plain treble 5161. The twin
    /// was writing the bare form at all four sites, so the 6 tracked books that use
    /// <c>treble_8</c> had twins that engraved a DIFFERENT CLEF — the exact "compile to other
    /// music" failure the twin exists to rule out, and it was invisible because the four
    /// clef names that are purely alphabetic do lex correctly bare.
    /// <para>
    /// The octave modifier is part of the NAME, not a separate token: make-clef-set matches
    /// the whole string against <c>^(.*)([_^])([^0-9a-zA-Z]*)([1-9][0-9]*)([^0-9a-zA-Z]*)$</c>
    /// and splits it itself. So the name has to REACH it in one piece — written bare,
    /// LilyPond's reader has already split it, and make-clef-set is handed "treble".
    /// </para>
    /// <para>
    /// Quoting unconditionally rather than only when the name has an underscore: quoted is
    /// LilyPond's documented spelling and is correct for every name (MEASURED — treble, bass,
    /// alto, tenor and treble_8 all pass, in both \clef and \cueClef), so nothing here has to
    /// reason about LilyPond's reader. ONE home for the rule, because four spellings of it is
    /// how this survived in three of them.
    /// </para>
    /// LILYPOND-REF: scm/parser-clef.scm:178-190 make-clef-set — takes clef-name as a string
    ///   and parses the octave modifier out of it.
    /// LILYPOND-REF: ly/music-functions-init.ly:535-538 make-cue-clef-set — cueClef declares
    ///   its argument (type) (string?) and hands it straight to it.
    /// </remarks>
    private static string LyClefName(string name) => "\"" + name + "\"";

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
            // ⚠️ NOT A SCRIPT, so it must answer here and never reach the `dir + glyph` tail
            // below: LilyPond's arpeggio is an EVENT on the chord (`<c e g>1\arpeggio`), and
            // `-\arpeggio` is not the same thing. Lily#'s ChordItem.HasArpeggio is a plain
            // bool with no direction, so there is nothing for a direction prefix to carry.
            // ⚠️ AND `<< … >>` IS A DIFFERENT CONSTRUCT — that is ArpeggioSyntax, a written-out
            // broken chord, and it has its own emitter. This is the stacked chord plus wavy
            // line. Confusing the two is what made the twins agree falsely.
            // LILYPOND-REF: lily/arpeggio-engraver.cc:73-80 Arpeggio_engraver::listen_arpeggio
            //   — an EVENT is listened for, not a script acknowledged, which is why the
            //   spelling is `<c e g>1\arpeggio`. ly/property-init.ly:67 is where the command
            //   itself is `#(make-music 'ArpeggioEvent)`.
            case "arpeggio": return "\\arpeggio";
            // Added 2026-08-05 (session 98). These five answer here and never reach
            // the `dir + glyph` tail below — for an UNFORCED spelling the tail would
            // prepend `-`, asserting a side the fixture never stated (the \arpeggio
            // remark above is the same mistake bought once already). MEASURED before
            // adding, on 2.26.0 (scratch probe, after-line-breaking dump per book):
            // each bare spelling engraves exactly ONE grob of its kind — \glissando a
            // Glissando, \startTrillSpan…\stopTrillSpan ONE TrillSpanner,
            // \laissezVibrer a LaissezVibrerTie, \repeatTie a RepeatTie.
            // LILYPOND-REF: ly/property-init.ly:378 glissando = #(make-music 'GlissandoEvent)
            // LILYPOND-REF: ly/spanners-init.ly:48-49 startTrillSpan / stopTrillSpan
            //   = #(make-span-event 'TrillSpanEvent START/STOP)
            // LILYPOND-REF: ly/declarations-init.ly:103-104 laissezVibrer / repeatTie
            //   = #(make-music 'LaissezVibrerEvent / 'RepeatTieEvent)
            case "glissando": return "\\glissando";
            case "starttrillspan": return "\\startTrillSpan";
            case "stoptrillspan": return "\\stopTrillSpan";
            // The half-tie events DO carry a meaningful written direction — ^/_ is
            // copied onto the tie (laissez-vibrer-engraver.cc:99-103, inherited by
            // Repeat_tie_engraver) and repeat-tie-chords.ly writes `d^\repeatTie` —
            // so a FORCED side must survive into the twin; unforced stays bare
            // (never `-`, same reason as above).
            case "laissezvibrer":
                return a.ForcedAbove switch
                {
                    true => "^\\laissezVibrer",
                    false => "_\\laissezVibrer",
                    null => "\\laissezVibrer",
                };
            case "repeattie":
                return a.ForcedAbove switch
                {
                    true => "^\\repeatTie",
                    false => "_\\repeatTie",
                    null => "\\repeatTie",
                };
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
            // Added 2026-08-05 (session 96). These four are TRUE SCRIPTS — they take a
            // direction, so they belong in this tail and not in the early switch that
            // \arpeggio needed. MEASURED before adding, on 2.26.0: `-\upbow`, `-\downbow`,
            // `-\flageolet`, `-\portato` and the forced `^`/`_` forms each engrave exactly
            // ONE Script grob, and portato's own default side is DOWN where the other three
            // are UP — which is why the neutral `-` is the right thing to write for an
            // unforced fixture: it lets LilyPond apply its own default instead of the twin
            // asserting a side the fixture never stated.
            // LILYPOND-REF: ly/script-init.ly:28,33,46,79 downbow/flageolet/portato/upbow — each is `name = #(make-articulation 'name)`, i.e. a post-event a direction sign may precede
            ArticulationType.UpBow => "\\upbow",
            ArticulationType.DownBow => "\\downbow",
            ArticulationType.Flageolet => "\\flageolet",
            ArticulationType.Portato => "\\portato",
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
        // ⚠️ THE INDENT IS WRITTEN, ALWAYS, and 0 is the interesting case. Lily# indents the
        // first system only when the score carries an instrument name, and then by exactly
        // LilyPond's paper default; LilyPond indents by that default whether or not anything
        // is written in it. So a NAMELESS twin left to the default is a different page from
        // its .lys by 8.535827 staff spaces on every horizontal measurement — which is the
        // kind of divergence that gets read as a spacing defect. See
        // LayoutEngine.CalculateIndentFromInstrumentNames, which records the same gap from
        // the other side.
        // ⚠️ `\mm`, NOT A BARE NUMBER. A bare number in \layout is read in MILLIMETRES, so
        // writing the staff-space figure silently produced a DIFFERENT page: measured on the
        // four-name twin, `indent = #8.535826771653543` engraved an effective indent of
        // 4.857400 (= 8.535827 mm ÷ 1.757355 mm per staff space) and the names moved with it,
        // while LilyPond compiled it without a murmur. Writing LilyPond's own spelling of its
        // own default removes the conversion instead of getting it right.
        // LILYPOND-REF: ly/paper-defaults-init.ly — indent = 15\mm.
        // ⚠️ printInitialRepeatBar IS WRITTEN, ALWAYS. Lily# prints a `|:` that opens the piece
        // (owner decision, session 328: the writer spelled it, so it is printed — the
        // lead-sheet convention the corpus follows), where LilyPond's default drops the
        // automatic opener at moment 0 (lily/bar-engraver.cc:432-449
        // Bar_engraver::pre_process_music, "At the start of a piece, we don't print any repeat
        // bars"). The twin says so in LilyPond's own words so the two pages agree; on a piece
        // that does not open with a repeat the setting changes nothing.
        // LILYPOND-REF: Documentation/en/notation/repeats.itely:160-172 printInitialRepeatBar.
        _sb.Append("  \\layout { indent = ")
           .Append(_instrumentNames.Count > 0 ? "15\\mm" : "0\\mm")
           .Append(" \\context { \\Score printInitialRepeatBar = ##t } }\n}\n");
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
        if (clef != null) sb.Append("\\clef ").Append(LyClefName(clef)).Append(' ');
        sb.Append('\\').Append(varName).Append(" }\n");
        return sb.ToString();
    }

    /// <summary>The part an ossia row names — the LAST token after the
    /// <c>as lines N</c> cut, the same read as
    /// <see cref="Svg.Collector.RenderSpecParser"/>'s ParseOssia (one home for
    /// the cut; the twin does not carry a line count, so only the slots move).</summary>
    private static string? OssiaPartName(OssiaRenderSyntax ossia)
    {
        var toks = OssiaTargetTokens(ossia);
        return toks.Count > 0 ? toks[^1].Text : null;
    }

    /// <summary>The clef word of <c>ossia [clef] part</c>, or null when the row is just
    /// <c>ossia part</c> (a lone word is the PART, never a clef).</summary>
    private static string? OssiaClef(OssiaRenderSyntax ossia)
    {
        var toks = OssiaTargetTokens(ossia);
        return toks.Count >= 2 ? toks[0].Text : null;
    }

    /// <summary>The ossia row's tokens after the keyword with the trailing
    /// <c>as lines N</c> selector cut off — the shared cut, so the part stays
    /// the last slot here exactly as it does for the renderer.</summary>
    private static List<SyntaxTokenNode> OssiaTargetTokens(OssiaRenderSyntax ossia)
    {
        var toks = new List<SyntaxTokenNode>();
        for (int i = 1; i < ossia.SlotCount; i++)
            if (ossia.GetChild(i) is SyntaxTokenNode t)
                toks.Add(t);
        Svg.Collector.RenderSpecParser.CutLinesSelector(toks);
        return toks;
    }

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
    /// True for <c>tab part as numbers</c> — fret digits only.
    /// </summary>
    /// <remarks>
    /// ⚠️ IT NO LONGER MIRRORS <c>RenderSpecParser</c>, IT SHARES WITH IT. This walked the
    /// tokens for the <c>as</c> itself and then compared <c>OrdinalIgnoreCase</c> — the same
    /// two lines, with the same case defect, as the page's copy; its own doc said "the two
    /// must agree, or the twin is drawn in the other mode from the page", which is the
    /// argument for one reading rather than two careful ones (HANDOFF §5.2.1②).
    /// </remarks>
    private static bool TabIsNumbersOnly(TabRenderSyntax tab) =>
        Semantics.TabRenderVocabularyValidator.IsNumbersOnly(tab);

    /// <summary>Fills <see cref="_instrumentNames"/> from the page's own reading of the
    /// render block.</summary>
    /// <remarks>
    /// ⚠️ A LONE TAB STAFF IS SKIPPED, because the page skips it: DrawInstrumentNames drops
    /// the label for a tab staff in its no-staff-groups branch (a single-staff score) and
    /// keeps it in the per-group branch. Mirroring that here is what keeps the twin the same
    /// picture; emitting it unconditionally would invent a divergence in the tab books, which
    /// are the ones least able to afford one.
    /// </remarks>
    private void CollectInstrumentNames(SyntaxTree tree)
    {
        _instrumentNames.Clear();
        var spec = RenderSpecParser.FindFirst(tree);
        if (spec is null) return;

        int staffItems = spec.Items.Count(
            i => i is SingleStaffSpec or GrandStaffRenderSpec or TabStaffSpec);

        void Take(StaffSpec st)
        {
            if (!string.IsNullOrEmpty(st.InstrumentName))
                _instrumentNames[st.VoiceName] = st.InstrumentName!;
        }

        foreach (var item in spec.Items)
            switch (item)
            {
                case SingleStaffSpec s: Take(s.Staff); break;
                case GrandStaffRenderSpec g:
                    foreach (var st in g.GrandStaff.Staves) Take(st);
                    break;
                case OssiaStaffSpec o: Take(o.Staff); break;
                case TabStaffSpec t when staffItems > 1: Take(t.Staff); break;
            }
    }

    /// <summary>The <c>\with { instrumentName = … }</c> clause a staff carries, or null.</summary>
    private string? InstrumentNameClause(string? partName) =>
        partName != null && _instrumentNames.TryGetValue(partName, out var n)
            ? "instrumentName = " + QuoteLilyPondString(n)
            : null;

    /// <summary>A Lily# label as a LilyPond string literal.</summary>
    /// <remarks>Only the two characters LilyPond's lexer treats specially inside <c>"…"</c>
    /// need escaping. Non-ASCII goes through as UTF-8, which is what LilyPond reads.</remarks>
    private static string QuoteLilyPondString(string s)
        => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

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
            var tabWith = new List<string>(2);
            if (tuning.Length > 0) tabWith.Add("stringTunings = #" + tuning);
            if (InstrumentNameClause(partName) is { } tabName) tabWith.Add(tabName);
            if (tabWith.Count > 0)
                sb.Append(" \\with { ").Append(string.Join(" ", tabWith)).Append(" }");
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
            sb.Append(indent).Append("\\new DrumStaff");
            if (InstrumentNameClause(partName) is { } drumName)
                sb.Append(" \\with { ").Append(drumName).Append(" }");
            sb.Append(" { \\").Append(varName).Append(" }\n");
        }
        else
        {
            sb.Append(indent).Append("\\new Staff");
            if (InstrumentNameClause(partName) is { } staffName)
                sb.Append(" \\with { ").Append(staffName).Append(" }");
            sb.Append(" { ");
            if (clef != null) sb.Append("\\clef ").Append(LyClefName(clef)).Append(' ');
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

    // The form this twin writes: the caller's choice, else the primary one. ⚠️ The reading
    // itself moved to ScoreForms — this used to match `main` case-INSENSITIVELY while the
    // MIDI and MusicXML exporters matched it exactly, which is two answers to one question.
    private FormDeclarationSyntax? PrimaryForm(CompilationUnitSyntax root)
        => Form ?? LilySharp.Core.Semantics.ScoreForms.Primary(root);

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

/// <summary>
/// Zero-width sentinel the form expansion plants at each section PLAY — the twin's
/// section boundary. Carries what the collector decides at the same spot
/// (MeasureCollector's reference arms): the occurrence's display label (null =
/// silent/suppressed — no mark) and whether a section-header key follows (which
/// overrides the score-key restore, the collector's else-if).
/// <c>LilyPondExporter.EmitSectionPlay</c> is the one consumer. Same shape as the
/// collector's <c>RelativeResetMarker</c>: a synthetic red over a zero-width green,
/// so it can travel a <c>List&lt;SyntaxNode&gt;</c> stream.
/// ⚠️ The DATA lives on the green (<see cref="SectionPlayGreen"/>), not the red:
/// <c>CreateEnding</c> rebuilds a volta ending's items as GREENS, and the red the
/// rebuilt tree hands back is a plain <c>GenericSyntaxNode</c> — the emitter matches
/// the green's type, which survives the rebuild, where a red-held payload silently
/// did not (the endings' marks and score-key restore were the exporter's last
/// silent-drop hole).
/// </summary>
internal sealed class SectionPlayMarker : SyntaxNode
{
    public SectionPlayMarker(string? markLabel, bool hasHeaderKey, bool hasHeaderTime,
        int octaveOffset = 0)
        : base(new SectionPlayGreen(markLabel, hasHeaderKey, hasHeaderTime, octaveOffset),
            parent: null, position: 0)
    {
    }
}

/// <summary>The section-play sentinel's green — the payload rides here so it survives
/// <c>CreateEnding</c>'s green rebuild (see <see cref="SectionPlayMarker"/>).</summary>
internal sealed class SectionPlayGreen : InternalSyntax.GreenNode
{
    /// <summary>The boxed label to engrave, or null for no mark (a silent
    /// <c>~Section</c> play, or an occurrence label written <c>""</c>).</summary>
    public string? MarkLabel { get; }

    /// <summary>True when the play's header registry carries a key — the boundary
    /// then takes THAT key and the score-key restore stays silent.</summary>
    public bool HasHeaderKey { get; }

    /// <summary>True when the play's header registry carries a <c>time</c> — the boundary
    /// then takes THAT meter and the score-meter restore stays silent. The twin of
    /// <see cref="HasHeaderKey"/>: the collector asks both questions at the same spot
    /// (MeasureCollector.ProcessSectionPrologue's header-time / header-key arms), and this
    /// carrier answered only the key until 2026-08-31.</summary>
    public bool HasHeaderTime { get; }

    /// <summary>The net octave shift written on the REFERENCE that opened this play
    /// (<c>~B'</c> = +1). The third thing this carrier had to be told: it was built for the
    /// key, taught the meter on 2026-08-31, and taught this on the same day — each time
    /// because the collector decides something at this spot that the twin could not
    /// re-derive from the flattened node list, which no longer holds the reference.</summary>
    public int OctaveOffset { get; }

    public SectionPlayGreen(string? markLabel, bool hasHeaderKey, bool hasHeaderTime,
        int octaveOffset = 0)
        : base(SyntaxKind.None, fullWidth: 0)
    {
        MarkLabel = markLabel;
        HasHeaderKey = hasHeaderKey;
        HasHeaderTime = hasHeaderTime;
        OctaveOffset = octaveOffset;
    }
}
