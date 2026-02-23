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

using System.Collections.Immutable;
using LilySharp.Core.Semantics.BoundTree;
using LilySharp.Core.Semantics.Symbols;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics.Binding;

/// <summary>
/// Binds a syntax tree to produce a bound score.
/// </summary>
/// <remarks>
/// The Binder performs:
/// 1. Symbol resolution (using SymbolTable)
/// 2. Structure expansion (repeats, alternatives)
/// 3. Relative pitch resolution
/// 4. Metadata extraction
/// </remarks>
public sealed class Binder
{
    private readonly List<SemanticDiagnostic> _diagnostics = new();
    private readonly RelativePitchResolver _pitchResolver = new();
    private Fraction _defaultDuration = Fraction.Quarter;
    private int _baseOctave = 4; // Base octave for section resets (from part/clef)

    /// <summary>
    /// Binds a syntax tree to produce a bound score.
    /// </summary>
    public BoundScore Bind(SyntaxTree tree, ImmutableSymbolTable symbols)
    {
        _diagnostics.Clear();
        _pitchResolver.Reset();
        _defaultDuration = Fraction.Quarter;

        var root = tree.GetRoot();

        // Extract metadata
        var metadata = ExtractMetadata(root);

        // Check if there's a structure to expand
        if (symbols.Structure != null)
        {
            return BindWithStructure(symbols, metadata);
        }

        // No structure - bind directly from root
        return BindDirect(root, symbols, metadata);
    }

    private BoundScore BindWithStructure(ImmutableSymbolTable symbols, BoundScoreMetadata metadata)
    {
        var expander = new StructureExpander(symbols, _diagnostics);
        var expanded = expander.Expand(symbols.Structure!.DeclarationSyntax);

        var voices = new List<BoundVoice>();
        foreach (var partName in expanded.PartNames)
        {
            var nodes = expanded.GetPart(partName);
            
            // Determine base octave from part definition
            _baseOctave = GetBaseOctaveForPart(partName, symbols);
            _pitchResolver.Reset(_baseOctave); // Reset for each voice with part's base octave
            var measures = BindMeasures(nodes);
            voices.Add(new BoundVoice(partName, measures));
        }

        // If no voices were created, create an empty one
        if (voices.Count == 0)
        {
            voices.Add(new BoundVoice("", ImmutableArray<BoundMeasure>.Empty));
        }

        return new BoundScore(
            voices.ToImmutableArray(),
            metadata,
            _diagnostics.ToImmutableArray());
    }

    private BoundScore BindDirect(SyntaxNode root, ImmutableSymbolTable symbols, BoundScoreMetadata metadata)
    {
        // Collect music from the syntax tree
        var musicNodes = new List<SyntaxNode>();

        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case NoteSyntax note:
                    musicNodes.Add(note);
                    break;

                case RestSyntax rest:
                    musicNodes.Add(rest);
                    break;

                case ChordSyntax chord:
                    musicNodes.Add(chord);
                    break;

                case BarlineSyntax barline:
                    musicNodes.Add(barline);
                    break;

                case TieSyntax tie:
                    musicNodes.Add(tie);
                    break;

                case SlurSyntax slur:
                    musicNodes.Add(slur);
                    break;
            }
        }

        var measures = BindMeasures(musicNodes.ToImmutableArray());
        var voice = new BoundVoice("", measures);

        return new BoundScore(
            ImmutableArray.Create(voice),
            metadata,
            _diagnostics.ToImmutableArray());
    }

    private ImmutableArray<BoundMeasure> BindMeasures(ImmutableArray<SyntaxNode> nodes)
    {
        var measures = new List<BoundMeasure>();
        var currentItems = new List<BoundMusic>();
        var startBarline = BarlineType.None;
        string? sectionLabel = null;
        SyntaxNode? measureStartSyntax = null;
        for (int i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i];

            // Check if next node is a tie
            bool hasTieAfter = i + 1 < nodes.Length && nodes[i + 1] is TieSyntax;
            // Check for slur markers
            bool hasSlurStartAfter = i + 1 < nodes.Length && nodes[i + 1] is SlurSyntax slurS && slurS.IsOpen;
            bool hasSlurEndAfter = i + 1 < nodes.Length && nodes[i + 1] is SlurSyntax slurE && !slurE.IsOpen;

            switch (node)
            {
                case SectionStartMarkerSyntax:
                    // Reset pitch resolver at section boundaries using part's base octave
                    _pitchResolver.Reset(_baseOctave);
                    break;

                case NoteSyntax note:
                    measureStartSyntax ??= note;
                    // Use hasTieAfter (next node is ~) or check articulations
                    bool hasTie = hasTieAfter || note.Articulations.OfType<TieSyntax>().Any();
                    // Check for slur start (from following '(' node or in articulations)
                    bool slurStartHere = hasSlurStartAfter || note.Articulations.OfType<SlurSyntax>().Any(s => s.IsOpen);
                    // Check for slur end (from following ')' node or in articulations)
                    bool slurEndHere = hasSlurEndAfter || note.Articulations.OfType<SlurSyntax>().Any(s => !s.IsOpen);
                    currentItems.Add(BindNote(note, hasTie, slurStartHere, slurEndHere));
                    break;

                case RestSyntax rest:
                    measureStartSyntax ??= rest;
                    currentItems.Add(BindRest(rest));
                    break;

                case ChordSyntax chord:
                    measureStartSyntax ??= chord;
                    currentItems.Add(BindChord(chord));
                    break;

                case TieSyntax:
                case SlurSyntax:
                    // Already processed with the note
                    break;

                case BarlineSyntax barline:
                    var barType = ParseBarlineType(barline.BarToken.Text);

                    if (barType == BarlineType.RepeatStart)
                    {
                        // Complete current measure if any, then set start barline for next
                        if (currentItems.Count > 0)
                        {
                            measures.Add(new BoundMeasure(
                                measureStartSyntax,
                                currentItems.ToImmutableArray(),
                                startBarline,
                                BarlineType.Single,
                                sectionLabel));
                            currentItems.Clear();
                            sectionLabel = null;
                        }
                        startBarline = BarlineType.RepeatStart;
                        measureStartSyntax = null;
                    }
                    else
                    {
                        // Complete measure with this barline
                        measures.Add(new BoundMeasure(
                            measureStartSyntax,
                            currentItems.ToImmutableArray(),
                            startBarline,
                            barType,
                            sectionLabel));
                        currentItems.Clear();
                        startBarline = BarlineType.None;
                        sectionLabel = null;
                        measureStartSyntax = null;
                    }
                    break;
            }
        }

        // Finalize any remaining items
        if (currentItems.Count > 0)
        {
            measures.Add(new BoundMeasure(
                measureStartSyntax,
                currentItems.ToImmutableArray(),
                startBarline,
                BarlineType.None,
                sectionLabel));
        }

        return measures.ToImmutableArray();
    }

    private BoundNote BindNote(NoteSyntax note, bool hasTieAfter, bool slurOpen, bool hasSlurEnd)
    {
        var pitch = _pitchResolver.Resolve(note.Pitch);

        int noteValue = note.Duration?.Value ?? (int)_defaultDuration.Denominator;
        if (note.Duration != null)
            _defaultDuration = Fraction.FromNoteValue(noteValue);

        int dots = note.Duration?.DotCount ?? 0;

        // Detect tie from following ~ syntax
        bool hasTieStart = hasTieAfter;
        bool hasTieEnd = false;

        // Detect slur from ( and ) markers
        bool hasSlurStart = slurOpen;

        // Extract tremolo beams from :8, :16, :32 suffix
        int tremoloBeams = GetTremoloBeams(note.Tremolo);

        return new BoundNote(
            note,
            pitch,
            Fraction.FromNoteValue(noteValue),
            dots,
            hasTieStart,
            hasTieEnd,
            hasSlurStart,
            hasSlurEnd,
            tremoloBeams);
    }

    private BoundRest BindRest(RestSyntax rest)
    {
        int noteValue = rest.Duration?.Value ?? (int)_defaultDuration.Denominator;
        if (rest.Duration != null)
            _defaultDuration = Fraction.FromNoteValue(noteValue);

        int dots = rest.Duration?.DotCount ?? 0;

        return new BoundRest(rest, Fraction.FromNoteValue(noteValue), dots);
    }

    private BoundChord BindChord(ChordSyntax chord)
    {
        var pitches = _pitchResolver.ResolveChord(chord.Pitches);
        var notes = pitches.Select(p => new BoundChordNote(
            p,
            HasTieStart: false,
            HasTieEnd: false)).ToList();

        int noteValue = chord.Duration?.Value ?? (int)_defaultDuration.Denominator;
        if (chord.Duration != null)
            _defaultDuration = Fraction.FromNoteValue(noteValue);

        int dots = chord.Duration?.DotCount ?? 0;

        // Extract tremolo beams from :8, :16, :32 suffix
        int tremoloBeams = GetTremoloBeams(chord.Tremolo);

        return new BoundChord(chord, notes, Fraction.FromNoteValue(noteValue), dots, tremoloBeams);
    }

    /// <summary>
    /// Converts a tremolo suffix token (:8, :16, :32) to beam count.
    /// </summary>
    private static int GetTremoloBeams(SyntaxTokenNode? tremoloToken)
    {
        if (tremoloToken == null)
            return 0;

        var text = tremoloToken.Text;
        return text switch
        {
            ":8" => 1,
            ":16" => 2,
            ":32" => 3,
            _ => 0
        };
    }

    private BoundScoreMetadata ExtractMetadata(SyntaxNode root)
    {
        string? title = null;
        string? composer = null;
        int? tempo = null;
        var timeSignature = new TimeSignature(4, 4);
        var keySignature = KeySignature.CMajor;
        string clef = "treble";

        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case MetadataDeclarationSyntax metadata:
                    var keyword = metadata.Keyword.ToLowerInvariant();
                    var values = metadata.Values.ToList();
                    switch (keyword)
                    {
                        case "title":
                            if (values.Count > 0 && values[0] is SyntaxTokenNode t)
                                title = t.Text.Trim('"');
                            break;
                        case "composer":
                            if (values.Count > 0 && values[0] is SyntaxTokenNode c)
                                composer = c.Text.Trim('"');
                            break;
                    }
                    break;

                case TempoDeclarationSyntax tempoDecl:
                    var tempoValues = tempoDecl.Values.ToList();
                    if (tempoValues.Count > 0 && tempoValues[0] is SyntaxTokenNode tempoToken
                        && int.TryParse(tempoToken.Text, out int tempoValue))
                        tempo = tempoValue;
                    break;

                case TimeSignatureSyntax timeSig:
                    timeSignature = new TimeSignature(timeSig.Beats, timeSig.BeatType);
                    break;

                case KeySignatureSyntax key:
                    keySignature = new KeySignature(CalculateKeySharps(key));
                    break;

                case ClefDeclarationSyntax clefDecl:
                    clef = clefDecl.ClefName.Text.ToLowerInvariant();
                    break;
            }
        }

        return new BoundScoreMetadata(title, composer, tempo, timeSignature, keySignature, clef);
    }

    private static int CalculateKeySharps(KeySignatureSyntax key)
    {
        var pitch = key.Pitch.PitchName.ToLowerInvariant();
        var mode = key.Mode.Text.ToLowerInvariant();

        // Major key sharps
        var majorSharps = new Dictionary<string, int>
        {
            ["c"] = 0, ["g"] = 1, ["d"] = 2, ["a"] = 3, ["e"] = 4, ["b"] = 5, ["fis"] = 6, ["cis"] = 7,
            ["f"] = -1, ["bes"] = -2, ["ees"] = -3, ["aes"] = -4, ["des"] = -5, ["ges"] = -6
        };

        // Handle accidentals in pitch name
        string basePitch;
        if (pitch.Contains("is"))
            basePitch = pitch.Replace("is", "") + "is";
        else if (pitch.Contains("es") || pitch.EndsWith("s"))
            basePitch = pitch.Replace("es", "").TrimEnd('s') + "es";
        else
            basePitch = pitch;

        if (!majorSharps.TryGetValue(basePitch, out var sharps))
            sharps = majorSharps.GetValueOrDefault(basePitch.Length > 0 ? basePitch[0].ToString() : "c", 0);

        // Adjust for minor (relative minor is 3 semitones down, equivalent to -3 in circle of fifths)
        if (mode == "minor")
            sharps -= 3;

        return sharps;
    }

    private static BarlineType ParseBarlineType(string text) => text switch
    {
        "|:" => BarlineType.RepeatStart,
        ":|" => BarlineType.RepeatEnd,
        ":|:" => BarlineType.RepeatBoth,
        "||" => BarlineType.Double,
        "|." => BarlineType.Final,
        _ => BarlineType.Single
    };

    /// <summary>
    /// Gets the base octave for a part based on its instrument or clef definition.
    /// </summary>
    private static int GetBaseOctaveForPart(string partName, ImmutableSymbolTable symbols)
    {
        // Try to find the part in the symbol table
        if (symbols.Parts.TryGetValue(partName, out var part))
        {
            // Check for explicit octave property
            var octaveStr = part.GetProperty("octave");
            if (octaveStr != null && int.TryParse(octaveStr, out var explicitOctave))
                return explicitOctave;

            // Check for instrument and derive octave from it
            var instrument = part.GetProperty("instrument");
            if (instrument != null)
            {
                var (_, octave) = InstrumentDefaults.GetDefaults(instrument);
                return octave;
            }

            // Check for clef and derive octave from it
            var clefStr = part.GetProperty("clef");
            if (clefStr != null)
            {
                var clefType = clefStr.ToLowerInvariant() switch
                {
                    "treble" => ClefType.Treble,
                    "bass" => ClefType.Bass,
                    "alto" => ClefType.Alto,
                    "tenor" => ClefType.Tenor,
                    _ => ClefType.Treble
                };
                return InstrumentDefaults.GetDefaultOctave(clefType);
            }
        }

        // Default to octave 4 (middle C region)
        return 4;
    }
}
