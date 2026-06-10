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

using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.MusicXml;

/// <summary>
/// Exports a syntax tree to MusicXML format.
/// Supports multi-section/multi-part scores, ties, slurs, grace notes,
/// dynamics, and ornaments.
/// </summary>
public sealed class MusicXmlExporter
{
    private const int DivisionsPerQuarter = 4;

    private int _currentOctave = 4;
    private int _currentStep = 0;     // c=0..b=6, for LilyPond relative-octave resolution (mirrors MidiExporter)
    private bool _tieToNextNote;      // a tie was seen; the next note/chord ends it (gets tie-stop)
    private Fraction _defaultDuration = Fraction.Quarter;
    private int _measureNumber = 1;
    private MusicXmlMeasure? _currentMeasure;
    private MusicXmlPart? _currentPart;
    private MusicXmlDocument? _document;

    private int _tempo = 120;
    private int _timeNumerator = 4;
    private int _timeDenominator = 4;
    private int _keyFifths = 0;
    private string _keyMode = "major";
    private string _clefSign = "G";
    private int _clefLine = 2;
    private string? _pendingDynamic;

    // Track parts across sections for multi-section support
    private readonly Dictionary<string, MusicXmlPart> _partsByName = new();

    // Variable/phrase resolution
    private readonly Dictionary<string, SyntaxNode> _variables = new();

    public MusicXmlDocument Export(SyntaxTree tree)
    {
        _document = new MusicXmlDocument();

        var root = tree.GetRoot();

        // Check if there are section declarations (multi-part)
        var hasSections = root.DescendantNodes().OfType<SectionDeclarationSyntax>().Any();

        if (!hasSections)
        {
            // Simple single-part mode — collect metadata first, then process music
            CollectMetadata(root);
            _currentPart = new MusicXmlPart { Name = "Part 1" };
            _document.Parts.Add(_currentPart);
            StartNewMeasure(addAttributes: true);
            ProcessNode(root);
            FlushCurrentMeasure();
        }
        else
        {
            // Multi-section mode: collect metadata first, then process sections
            CollectMetadata(root);
            ProcessSections(root);
        }

        return _document;
    }

    private void CollectMetadata(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case MetadataDeclarationSyntax metadata:
                    ProcessMetadata(metadata);
                    break;
                case TimeSignatureSyntax timeSig:
                    ProcessTimeSignature(timeSig);
                    break;
                case TempoDeclarationSyntax tempo:
                    ProcessTempo(tempo);
                    break;
                case KeySignatureSyntax key:
                    ProcessKeySignature(key);
                    break;
                case ClefDeclarationSyntax clef:
                    ProcessClef(clef);
                    break;
                case PhraseDeclarationSyntax phrase:
                    _variables[phrase.Name.Text] = phrase.Body;
                    break;
                case VariableDeclarationSyntax varDecl:
                    _variables[varDecl.Name.Text] = varDecl.Expression;
                    break;
            }
        }
    }

    private void ProcessSections(SyntaxNode root)
    {
        foreach (var section in root.DescendantNodes().OfType<SectionDeclarationSyntax>())
        {
            // Each section may contain part blocks
            var partBlocks = section.DescendantNodes().OfType<PartBlockSyntax>().ToList();

            if (partBlocks.Count > 0)
            {
                foreach (var partBlock in partBlocks)
                {
                    ProcessPartBlock(partBlock);
                }
            }
            else
            {
                // Section without named parts → treat as default part
                EnsurePart("Part 1");
                ProcessNode(section);
                FlushCurrentMeasure();
            }
        }
    }

    private void ProcessPartBlock(PartBlockSyntax partBlock)
    {
        var partName = partBlock.Name;
        EnsurePart(partName);

        // Reset state for this part's continuation
        _currentOctave = 4;
        _currentStep = 0;
        _tieToNextNote = false;
        _defaultDuration = Fraction.Quarter;
        _pendingDynamic = null;

        // If this is the first measure for this part, add attributes
        bool isFirst = _currentPart!.Measures.Count == 0;
        StartNewMeasure(addAttributes: isFirst);

        // Process the content inside the part block
        for (int i = 0; i < partBlock.SlotCount; i++)
        {
            var child = partBlock.GetChild(i);
            if (child != null && child is not SyntaxTokenNode)
                ProcessNode(child);
        }

        FlushCurrentMeasure();
    }

    private void EnsurePart(string name)
    {
        if (_partsByName.TryGetValue(name, out var existing))
        {
            _currentPart = existing;
            _measureNumber = existing.Measures.Count + 1;
        }
        else
        {
            _currentPart = new MusicXmlPart { Name = name };
            _document!.Parts.Add(_currentPart);
            _partsByName[name] = _currentPart;
            _measureNumber = 1;
        }
    }

    private void FlushCurrentMeasure()
    {
        if (_currentMeasure != null && _currentMeasure.Notes.Count > 0 && _currentPart != null)
        {
            _currentPart.Measures.Add(_currentMeasure);
        }
        _currentMeasure = null;
    }

    private void StartNewMeasure(bool addAttributes = false)
    {
        _currentMeasure = new MusicXmlMeasure { Number = _measureNumber++ };

        if (addAttributes)
        {
            _currentMeasure.Attributes = new MusicXmlAttributes
            {
                Divisions = DivisionsPerQuarter,
                TimeBeats = _timeNumerator,
                TimeBeatType = _timeDenominator,
                KeyFifths = _keyFifths,
                KeyMode = _keyMode,
                ClefSign = _clefSign,
                ClefLine = _clefLine
            };

            _currentMeasure.Direction = new MusicXmlDirection { Tempo = _tempo };
        }
    }

    private void ProcessNode(SyntaxNode node)
    {
        switch (node)
        {
            case CompilationUnitSyntax unit:
                foreach (var member in unit.Members)
                    ProcessNode(member);
                break;

            case TimeSignatureSyntax timeSig:
                ProcessTimeSignature(timeSig);
                break;

            case TempoDeclarationSyntax tempo:
                ProcessTempo(tempo);
                break;

            case MetadataDeclarationSyntax metadata:
                ProcessMetadata(metadata);
                break;

            case KeySignatureSyntax key:
                ProcessKeySignature(key);
                break;

            case ClefDeclarationSyntax clef:
                ProcessClef(clef);
                break;

            case MusicBlockSyntax block:
                foreach (var item in block.Items)
                    ProcessNode(item);
                break;

            case NoteSyntax note:
                ProcessNote(note);
                break;

            case ChordSyntax chord:
                ProcessChord(chord);
                break;

            case RestSyntax rest:
                ProcessRest(rest);
                break;

            case BarlineSyntax:
                if (_currentMeasure != null && _currentPart != null)
                {
                    _currentPart.Measures.Add(_currentMeasure);
                    StartNewMeasure();
                }
                break;

            case DynamicSyntax dynamic:
                _pendingDynamic = dynamic.DynamicToken.Text;
                break;

            case TieSyntax:
                // Tie follows a note — mark the last note as tie start, and flag
                // the next note/chord so it emits the matching tie-stop.
                if (_currentMeasure != null && _currentMeasure.Notes.Count > 0)
                    _currentMeasure.Notes[^1].TieStart = true;
                _tieToNextNote = true;
                break;

            case SlurSyntax slur:
                // Slur follows a note — mark start/stop on the last note
                if (_currentMeasure != null && _currentMeasure.Notes.Count > 0)
                {
                    if (slur.IsOpen)
                        _currentMeasure.Notes[^1].SlurStart = true;
                    else
                        _currentMeasure.Notes[^1].SlurStop = true;
                }
                break;

            case GraceExpressionSyntax grace:
                ProcessGraceNotes(grace);
                break;

            case VariableReferenceSyntax varRef:
                if (_variables.TryGetValue(varRef.Name.Text, out var varBody))
                    ProcessNode(varBody);
                break;

            case PhraseDeclarationSyntax:
            case VariableDeclarationSyntax:
            case PartDeclarationSyntax:
            case SectionDeclarationSyntax:
            case StructureDeclarationSyntax:
                // Skip declarations — they're handled elsewhere
                break;

            default:
                for (int i = 0; i < node.SlotCount; i++)
                {
                    var child = node.GetChild(i);
                    if (child != null && child is not SyntaxTokenNode)
                        ProcessNode(child);
                }
                break;
        }
    }

    private void ProcessTimeSignature(TimeSignatureSyntax timeSig)
    {
        _timeNumerator = timeSig.Beats;
        _timeDenominator = timeSig.BeatType;
    }

    private void ProcessTempo(TempoDeclarationSyntax tempo)
    {
        if (tempo.Bpm is int bpm)
            _tempo = bpm;
    }

    private void ProcessMetadata(MetadataDeclarationSyntax metadata)
    {
        if (_document == null) return;

        var keyword = metadata.Keyword.ToLowerInvariant();

        if (keyword == "title" && metadata.StringValue is string title)
            _document.Title = title;
        else if (keyword == "composer" && metadata.StringValue is string composer)
            _document.Composer = composer;
    }

    private void ProcessKeySignature(KeySignatureSyntax key)
    {
        var pitch = key.Pitch?.ToFullString().Trim().ToLower();
        var isMajor = key.IsMajor;

        _keyFifths = pitch switch
        {
            "c" => isMajor ? 0 : -3,
            "g" => isMajor ? 1 : -2,
            "d" => isMajor ? 2 : -1,
            "a" => isMajor ? 3 : 0,
            "e" => isMajor ? 4 : 1,
            "b" => isMajor ? 5 : 2,
            "fis" => isMajor ? 6 : 3,
            "cis" => isMajor ? 7 : 4,
            "f" => isMajor ? -1 : -4,
            "bes" => isMajor ? -2 : -5,
            "ees" => isMajor ? -3 : -6,
            "aes" => isMajor ? -4 : -7,
            "des" => isMajor ? -5 : -8,
            "ges" => isMajor ? -6 : -9,
            _ => 0
        };
        _keyMode = isMajor ? "major" : "minor";
    }

    private void ProcessClef(ClefDeclarationSyntax clef)
    {
        var clefName = clef.ClefName?.Text.ToLower();
        (_clefSign, _clefLine) = clefName switch
        {
            "treble" => ("G", 2),
            "bass" => ("F", 4),
            "alto" => ("C", 3),
            "tenor" => ("C", 4),
            _ => ("G", 2)
        };
    }

    private void ProcessNote(NoteSyntax note)
    {
        if (_currentMeasure == null) return;

        var (step, alter) = ParsePitch(note.Pitch);
        int targetOctave = ResolveRelativeOctave(note.Pitch);

        var duration = GetDuration(note.Duration);
        int durationTicks = FractionToTicks(duration);
        var (type, dots) = GetNoteType(duration);

        // Emit pending dynamic as direction before the note
        EmitPendingDynamic();

        var xmlNote = new MusicXmlNote
        {
            Step = step,
            Alter = alter,
            Octave = targetOctave,
            Duration = durationTicks,
            Type = type,
            Dots = dots
        };

        // Process articulations and slurs
        ProcessArticulations(note.Articulations, xmlNote);

        // Tie pairing: a preceding '~' ends on this note (tie-stop); a '~' on
        // this note (sibling or articulation) starts a tie to the next note.
        if (_tieToNextNote) { xmlNote.TieStop = true; _tieToNextNote = false; }
        if (note.Articulations.OfType<TieSyntax>().Any()) { xmlNote.TieStart = true; _tieToNextNote = true; }

        _currentMeasure.Notes.Add(xmlNote);
    }

    private void ProcessChord(ChordSyntax chord)
    {
        if (_currentMeasure == null) return;

        var pitches = chord.Pitches.ToList();
        if (pitches.Count == 0) return;

        var duration = GetDuration(chord.Duration);
        int durationTicks = FractionToTicks(duration);
        var (type, dots) = GetNoteType(duration);

        // Emit pending dynamic as direction before the chord
        EmitPendingDynamic();

        // LilyPond relative chords: each note is relative to the PREVIOUS note in
        // the chord (state advances per pitch); the note after the chord is relative
        // to the FIRST pitch. Matches MidiExporter and MeasureCollector.
        int firstStep = _currentStep, firstOctave = _currentOctave;
        bool isFirst = true;
        foreach (var pitch in pitches)
        {
            var (step, alter) = ParsePitch(pitch);
            int targetOctave = ResolveRelativeOctave(pitch); // advances state per pitch

            if (isFirst) { firstStep = _currentStep; firstOctave = _currentOctave; }

            var xmlNote = new MusicXmlNote
            {
                Step = step,
                Alter = alter,
                Octave = targetOctave,
                Duration = durationTicks,
                Type = type,
                Dots = dots,
                IsChord = !isFirst
            };

            // Add articulations + tie pairing only on the first note of the chord.
            if (isFirst)
            {
                ProcessArticulations(chord.Articulations, xmlNote);
                if (_tieToNextNote) { xmlNote.TieStop = true; _tieToNextNote = false; }
                if (chord.Articulations.OfType<TieSyntax>().Any()) { xmlNote.TieStart = true; _tieToNextNote = true; }
                isFirst = false;
            }

            _currentMeasure.Notes.Add(xmlNote);
        }

        // Continue from the first chord note (LilyPond: next note is relative to
        // the chord's first pitch).
        _currentStep = firstStep;
        _currentOctave = firstOctave;
    }

    private void ProcessRest(RestSyntax rest)
    {
        if (_currentMeasure == null) return;

        var duration = GetDuration(rest.Duration);
        int durationTicks = FractionToTicks(duration);
        var (type, dots) = GetNoteType(duration);

        var xmlNote = new MusicXmlNote
        {
            IsRest = true,
            Duration = durationTicks,
            Type = type,
            Dots = dots
        };

        _currentMeasure.Notes.Add(xmlNote);
    }

    private void ProcessGraceNotes(GraceExpressionSyntax grace)
    {
        if (_currentMeasure == null) return;

        bool isAcciaccatura = grace.IsAcciaccatura;

        foreach (var item in grace.Body.Items)
        {
            if (item is NoteSyntax note)
            {
                var (step, alter) = ParsePitch(note.Pitch);
                int targetOctave = ResolveRelativeOctave(note.Pitch);

                var duration = GetDuration(note.Duration);
                var (type, _) = GetNoteType(duration);

                var xmlNote = new MusicXmlNote
                {
                    IsGrace = true,
                    IsSlash = isAcciaccatura,
                    Step = step,
                    Alter = alter,
                    Octave = targetOctave,
                    Type = type
                };

                _currentMeasure.Notes.Add(xmlNote);
            }
        }
    }

    private void ProcessArticulations(IEnumerable<SyntaxNode> articulations, MusicXmlNote xmlNote)
    {
        foreach (var artic in articulations)
        {
            if (artic is ArticulationSyntax articulation)
            {
                var articName = MapArticulation(articulation.Type);
                if (articName != null)
                    xmlNote.Articulations.Add(articName);

                var ornamentName = MapOrnament(articulation.Type);
                if (ornamentName != null)
                    xmlNote.Ornaments.Add(ornamentName);
            }
            else if (artic is DynamicSyntax dynamic)
            {
                _pendingDynamic = dynamic.DynamicToken.Text;
            }
            else if (artic is SlurSyntax slur)
            {
                if (slur.IsOpen)
                    xmlNote.SlurStart = true;
                else
                    xmlNote.SlurStop = true;
            }
        }
    }

    private void EmitPendingDynamic()
    {
        if (_pendingDynamic != null && _currentMeasure != null)
        {
            _currentMeasure.Directions.Add(new MusicXmlDirection
            {
                DynamicType = _pendingDynamic,
                Placement = "below"
            });
            _pendingDynamic = null;
        }
    }

    private static string? MapArticulation(ArticulationType type)
    {
        return type switch
        {
            ArticulationType.Staccato => "staccato",
            ArticulationType.Accent => "accent",
            ArticulationType.Tenuto => "tenuto",
            ArticulationType.Marcato => "strong-accent",
            ArticulationType.Fermata => "fermata",
            ArticulationType.Portato => "detached-legato",
            _ => null
        };
    }

    private static string? MapOrnament(ArticulationType type)
    {
        return type switch
        {
            ArticulationType.Trill => "trill-mark",
            ArticulationType.Mordent => "mordent",
            ArticulationType.Prall => "inverted-mordent",
            ArticulationType.Turn => "turn",
            ArticulationType.InvertedTurn => "inverted-turn",
            ArticulationType.PrallTriller => "inverted-mordent",
            _ => null
        };
    }

    private (string step, int alter) ParsePitch(PitchSyntax pitch)
    {
        string step = char.ToUpper(pitch.BaseName).ToString();
        return (step, pitch.AccidentalOffset);
    }

    /// <summary>
    /// Resolves the absolute octave of a pitch using LilyPond's relative-octave
    /// rule (nearest octave to the previous pitch, within a fourth), then applies
    /// the explicit ' / , offset. Mirrors <c>MidiExporter.CalculateRelativeMidiPitch</c>
    /// so MIDI and MusicXML octaves agree. Updates the running step/octave state.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/pitch.cc — relative octave (closest interval).</remarks>
    private int ResolveRelativeOctave(PitchSyntax pitch)
    {
        int noteName = StepIndex(pitch.BaseName);

        // Closest-octave rule + explicit '/, offset — shared with the collector
        // and the MIDI exporter (RelativeOctave is the single source of truth).
        int targetOctave = RelativeOctave.Resolve(
            _currentStep, _currentOctave, noteName, pitch.OctaveOffset);

        _currentStep = noteName;
        _currentOctave = targetOctave;
        return targetOctave;
    }

    private static int StepIndex(char baseName) => RelativeOctave.StepIndex(baseName);

    private Fraction GetDuration(DurationSyntax? duration)
    {
        if (duration == null) return _defaultDuration;
        _defaultDuration = duration.ToFraction();
        return _defaultDuration;
    }

    private int FractionToTicks(Fraction frac)
    {
        return (int)(frac.Numerator * DivisionsPerQuarter * 4 / frac.Denominator);
    }

    private (string type, int dots) GetNoteType(Fraction duration)
    {
        int dots = 0;
        int baseDenom = (int)duration.Denominator;

        // Check for dotted notes (3/8 = dotted quarter, 3/4 = dotted half, etc.)
        if (duration.Numerator == 3 && duration.Denominator % 2 == 0)
        {
            dots = 1;
            baseDenom = (int)(duration.Denominator / 2);
        }

        // Check for double-dotted notes (7/16 = double-dotted quarter, etc.)
        if (duration.Numerator == 7 && duration.Denominator % 4 == 0)
        {
            dots = 2;
            baseDenom = (int)(duration.Denominator / 4);
        }

        string type = baseDenom switch
        {
            1 => "whole",
            2 => "half",
            4 => "quarter",
            8 => "eighth",
            16 => "16th",
            32 => "32nd",
            64 => "64th",
            128 => "128th",
            _ => "quarter"
        };

        return (type, dots);
    }
}
