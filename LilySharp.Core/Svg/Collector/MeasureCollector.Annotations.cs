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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Svg.Collector;

// Annotation collection for MeasureCollector: figured bass, chord names,
// cross-staff, grob overrides/reverts, articulations & text annotations, and
// trill-spanner pairing. Split out of MeasureCollector.cs as a partial class;
// same instance state, no behavior change.
public sealed partial class MeasureCollector
{
    /// <summary>
    /// Collects figured bass annotations from note/chord modifiers.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/figured-bass-engraver.cc - listen_bass_figure
    /// Syntax: @fig.6 (single), @fig.6.4 (two figures), @fig.6.s (with sharp)
    /// </remarks>
    private void CollectFiguredBass(SyntaxNode node, int measureIndex, int itemIndex)
    {
        var articulations = ArticulationsOf(node);

        foreach (var child in articulations)
        {
            if (child is MusicMarkSyntax markSyntax)
            {
                var figures = FiguredBassItem.ParseFigures(markSyntax.MarkName);
                if (figures != null)
                {
                    _figuredBasses.Add(new FiguredBassItem(
                        figures.Value,
                        measureIndex,
                        itemIndex,
                        markSyntax.Position,
                        _currentStaffIndex));
                }
            }
        }
    }

    /// <summary>
    /// Collects chord name annotations (@chord.TEXT) from a note or chord's articulations.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/scheme-engravers.scm:1309 - Current_chord_text_engraver
    /// Syntax: @chord.Cm7, @chord.Bb7, @chord.Am
    /// </remarks>
    private void CollectChordNames(SyntaxNode node, int measureIndex, int itemIndex)
    {
        var articulations = ArticulationsOf(node);

        foreach (var child in articulations)
        {
            if (child is MusicMarkSyntax markSyntax)
            {
                var chordText = ChordNameItem.ParseChordName(markSyntax.MarkName, out var structure);
                if (chordText != null)
                    _chordNameCollector.AddInline(
                        chordText, measureIndex, itemIndex, markSyntax.Position, _currentStaffIndex, structure);
            }
        }
    }

    /// <summary>
    /// Detects @cross annotation on a note or chord for cross-staff rendering.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc:1451-1459 - cross-staff detection
    /// Syntax: @cross marks a note for rendering on the other staff in a grand staff.
    ///
    /// In a grand staff context:
    /// - If voice is on staff 0 (treble), @cross moves to staff 1 (bass)
    /// - If voice is on staff 1 (bass), @cross moves to staff 0 (treble)
    /// The TargetStaffIndex is resolved later during layout based on voice assignment.
    /// Here we use 0 as a placeholder (actual target resolved by layout engine).
    /// </remarks>
    private void CollectCrossStaff(SyntaxNode node, int measureIndex, int itemIndex)
    {
        var articulations = ArticulationsOf(node);

        foreach (var child in articulations)
        {
            // @cross is parsed as ArticulationSyntax (single Identifier, no dot)
            if (child is ArticulationSyntax artSyntax && artSyntax.NameToken.Text == "cross")
            {
                _crossStaffItems.Add(new CrossStaffItem(
                    measureIndex,
                    itemIndex,
                    0,
                    artSyntax.Position));
                return;
            }
        }
    }

    /// <summary>
    /// Collects a grob property override from an OverrideDeclarationSyntax.
    /// LILYPOND-REF: lily/context-property.cc (push)
    /// </summary>
    private void CollectOverride(OverrideDeclarationSyntax node, int measureIndex, int itemIndex, bool isOnce)
    {
        string grobType = node.GrobName.Text;
        string propertyName = node.PropertyName.Text;
        // A quoted value (e.g. color = "red") stores its CONTENT, not the quotes, so the
        // resolver's GetString/ParseColor see "red" — matching the bare-identifier form.
        // A combined negative number keeps any interior whitespace ("- 5") in its token
        // text for round-tripping, so strip it here for the numeric reparse (GetInt /
        // GetDouble). Identifiers and positive integers carry no interior whitespace, so
        // stripping is a no-op for them.
        string value = node.ValueToken.Kind == SyntaxKind.StringLiteral
            ? node.ValueToken.Text.Trim('"')
            : string.Concat(node.ValueToken.Text.Where(c => !char.IsWhiteSpace(c)));
        _grobOverrides.Add(new GrobOverride(grobType, propertyName, value, measureIndex, itemIndex, isOnce));
    }

    /// <summary>
    /// Collects a grob property revert from a RevertDeclarationSyntax.
    /// LILYPOND-REF: lily/context-property.cc (pop)
    /// </summary>
    private void CollectRevert(RevertDeclarationSyntax node, int measureIndex, int itemIndex)
    {
        string grobType = node.GrobName.Text;
        string propertyName = node.PropertyName.Text;
        _grobReverts.Add(new GrobRevert(grobType, propertyName, measureIndex, itemIndex));
    }

    /// <summary>
    /// Gets the feathered beam direction from a note's articulations.
    /// Returns 0 (none), 1 (right/accel), or -1 (left/rit).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: beam.cc:1039-1082 grow-direction
    /// Syntax: @feather.right (accelerando) or @feather.left (ritardando)
    /// </remarks>
    private static int GetFeatherDirection(SyntaxNode node)
    {
        if (node is not NoteSyntax note)
            return 0;

        foreach (var child in note.Articulations)
        {
            if (child is MusicMarkSyntax markSyntax)
            {
                var name = markSyntax.MarkName.ToLowerInvariant();
                if (name == "feather.right" || name == "feather.accel")
                    return 1;
                if (name == "feather.left" || name == "feather.rit")
                    return -1;
            }
        }
        return 0;
    }

    /// <summary>
    /// Collects articulation marks from note/chord modifiers.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: script-engraver.cc:92-125 Script_engraver::acknowledge_note_head
    /// </remarks>
    private void CollectArticulations(SyntaxNode node, int measureIndex, int itemIndex, bool stemUp,
        string? editorialAccidental = null, Fraction anchorTiming = default)
    {
        var articulations = node switch
        {
            NoteSyntax note => note.Articulations,
            ChordSyntax chord => chord.Articulations,
            RestSyntax rest => rest.Articulations,
            _ => Enumerable.Empty<SyntaxNode>()
        };

        foreach (var articulation in articulations)
        {
            if (articulation is ArticulationSyntax articulationSyntax)
            {
                var type = articulationSyntax.Type;
                if (type != ArticulationType.None)
                {
                    // LILYPOND-REF: script-interface.cc:23-45 direction calculation
                    // Articulations go opposite to stem direction by default
                    bool isAbove = !stemUp;

                    // Fermata and ornaments always go above
                    // LILYPOND-REF: define-grobs.scm:1365 fermata: direction = UP
                    // LILYPOND-REF: define-grobs.scm:2175 ornaments: direction = UP
                    if (type == ArticulationType.Fermata ||
                        type == ArticulationType.Trill ||
                        type == ArticulationType.Mordent ||
                        type == ArticulationType.Prall ||
                        type == ArticulationType.Turn ||
                        type == ArticulationType.InvertedTurn ||
                        type == ArticulationType.PrallTriller ||
                        // Breathing signs always sit at the top of the staff.
                        type == ArticulationType.Breath ||
                        type == ArticulationType.Caesura)
                    {
                        isAbove = true;
                    }

                    // An explicit '.up' / '.down' qualifier overrides the automatic side.
                    if (articulationSyntax.ForcedAbove is bool forcedAbove)
                        isAbove = forcedAbove;

                    _articulations.Add(new ArticulationItem(type, measureIndex, itemIndex, isAbove, articulationSyntax.Position, _currentStaffIndex));
                }
                else
                {
                    // Check for trill spanner start/stop
                    // LILYPOND-REF: scm/scheme-engravers.scm — \startTrillSpan / \stopTrillSpan
                    var nameText = articulationSyntax.NameToken.Text;
                    var nameLower = nameText.ToLowerInvariant();
                    if (nameLower == "starttrillspan")
                    {
                        _trillSpannerEvents.Add((true, measureIndex, itemIndex, articulationSyntax.Position, _currentStaffIndex));
                    }
                    else if (nameLower == "stoptrillspan")
                    {
                        _trillSpannerEvents.Add((false, measureIndex, itemIndex, articulationSyntax.Position, _currentStaffIndex));
                    }
                    else if (nameLower == "courtesy")
                    {
                        // LILYPOND-REF: lily/accidental.cc:147-148 — parenthesized property
                        // Explicit @courtesy annotation forces courtesy (parenthesized) accidental
                        _courtesySourcePositions.Add(node.Position);
                    }
                    else if (nameLower == "editorial" && editorialAccidental != null)
                    {
                        // Editorial (suggestion) accidental: a small accidental
                        // ABOVE the note; the kind was resolved in CreateNoteItem.
                        // LILYPOND-REF: scm/define-grobs.scm:96-123 AccidentalSuggestion
                        _articulations.Add(new ArticulationItem(
                            ArticulationItem.EditorialTypeFor(editorialAccidental),
                            measureIndex, itemIndex, isAbove: true,
                            articulationSyntax.Position, _currentStaffIndex));
                    }
                    else
                    {
                        // Check if this articulation is a MusicMark (cresc, rit, mark.A, ottava, ped, etc.)
                        var markType = MusicMarkItem.ParseMarkName(nameText);
                        if (markType != null)
                        {
                            if (markType.Value == MusicMarkType.Rehearsal)
                            {
                                string text = MusicMarkItem.ParseRehearsalText(nameText);
                                _musicMarks.Add(new MusicMarkItem(MusicMarkType.Rehearsal, text, measureIndex, articulationSyntax.Position, itemIndex, anchorTiming) { StaffIndex = _currentStaffIndex });
                            }
                            else
                            {
                                // Anchor to the host note's column so note-attached
                                // marks (e.g. pedal "Ped.") sit at the note, not the
                                // measure start.
                                _musicMarks.Add(new MusicMarkItem(markType.Value, measureIndex, articulationSyntax.Position, itemIndex, anchorTiming) { StaffIndex = _currentStaffIndex });
                            }
                        }
                    }
                }
            }
            else if (articulation is MusicMarkSyntax markSyntax)
            {
                // Handle compound mark syntax: @trillSpan.start / @trillSpan.stop
                var markName = markSyntax.MarkName.ToLowerInvariant();
                if (markName == "trillspan.start")
                {
                    _trillSpannerEvents.Add((true, measureIndex, itemIndex, markSyntax.Position, _currentStaffIndex));
                }
                else if (markName == "trillspan.stop")
                {
                    _trillSpannerEvents.Add((false, measureIndex, itemIndex, markSyntax.Position, _currentStaffIndex));
                }
                else if (markName.StartsWith("pluck.") && markName.Length == 7
                         && markName[6] is 'p' or 'i' or 'm' or 'a')
                {
                    // p-i-m-a right-hand fingering, printed BELOW the note.
                    _articulations.Add(new ArticulationItem(
                        ArticulationType.Pluck, measureIndex, itemIndex, false,
                        markSyntax.Position, _currentStaffIndex)
                    { PluckLetter = markName[6..] });
                }
                else if (markName.StartsWith("frame."))
                {
                    // @frame(x32010) — chord diagram above the note.
                    // LILYPOND-REF: MusicXML <frame>; LP \fret-diagram.
                    var spec = markName[6..];
                    if (spec.Length is >= 4 and <= 8
                        && spec.All(ch => ch is 'x' or 'o' or (>= '0' and <= '9')))
                        _articulations.Add(new ArticulationItem(
                            ArticulationType.FretFrame, measureIndex, itemIndex, true,
                            markSyntax.Position, _currentStaffIndex)
                        { FrameSpec = spec });
                }
                else if (markName.StartsWith("bend."))
                {
                    // @bend(full|half|N) — guitar bend-up, N in semitones.
                    // LILYPOND-REF: MusicXML <bend><bend-alter> semantics.
                    int? semis = markName[5..] switch
                    {
                        "half" => 1,
                        "full" => 2,
                        var s when int.TryParse(s, out int n) && n is > 0 and <= 12 => n,
                        _ => null,
                    };
                    if (semis is { } sv)
                        _articulations.Add(new ArticulationItem(
                            ArticulationType.Bend, measureIndex, itemIndex, true,
                            markSyntax.Position, _currentStaffIndex)
                        { BendSemitones = sv });
                }
                else if (markName.StartsWith("notehead."))
                {
                    // Consumed by ExtractNoteheadStyle at item creation — not a
                    // printed mark.
                }
                else if (markName.StartsWith("finger."))
                {
                    // LILYPOND-REF: lily/fingering-engraver.cc — finger event attaches to
                    // the host note. Keyed by the note's source position.
                    if (ParseFingerMark(markSyntax) is { } finger)
                        _fingeringByPosition[node.Position] = finger;
                }
                else if (markName == "text" || markName.StartsWith("text."))
                {
                    // @text("dolce")[.up/.down] — free expressive text on the host
                    // note. Rides the DynamicText pipeline as expressive text: it
                    // is NOT a dynamic level, so hairpins ignore it and MIDI is
                    // untouched. LILYPOND-REF: TextScript (LP's c^"text"/c_"text"),
                    // direction DOWN by default.
                    if (ExtractTextAnnotation(markSyntax, out bool forcedUp) is { } freeText)
                        _dynamics.Add(new DynamicItem(
                            freeText, measureIndex, itemIndex, markSyntax.Position, _currentStaffIndex)
                        {
                            IsAbove = forcedUp,
                        });
                }
                else if (MusicMarkItem.ParseMarkName(markSyntax.MarkName) is { } compoundMark
                         && (IsNoteAnchoredPedalMark(compoundMark) || IsOttavaMark(compoundMark)))
                {
                    // A compound PEDAL mark (e.g. @ped.off) or a compound OTTAVA mark
                    // (the down forms @ottava(bassa) / @quindicesima(bassa)) written ON
                    // a note. Like @ped and the plain @ottava above, anchor it to the
                    // host note's column via itemIndex/anchorTiming and — crucially —
                    // carry _currentStaffIndex so the bracket and its octave
                    // transposition land on the AUTHORING staff. Without this the
                    // statement-level handler created it with no staff (defaulting to
                    // staff 0), so on a grand staff a lower-staff 8vb was attributed to
                    // the top staff. The statement-level handler then de-dupes this by
                    // source position. Non-pedal, non-ottava compound marks (e.g.
                    // @mark.A rehearsal) are left to that handler, which extracts text.
                    // LILYPOND-REF: piano-pedal-engraver.cc / ottava-engraver.cc.
                    _musicMarks.Add(new MusicMarkItem(
                        compoundMark, measureIndex, markSyntax.Position, itemIndex, anchorTiming) { StaffIndex = _currentStaffIndex });
                }
            }
        }
    }

    /// <summary>
    /// The string payload of a <c>@text("…")</c> annotation (quotes stripped),
    /// plus whether a trailing <c>.up</c> forces it above the staff. Null when
    /// the annotation carries no string literal (e.g. a bare <c>@text</c>).
    /// </summary>
    private static string? ExtractTextAnnotation(MusicMarkSyntax mark, out bool forcedUp)
    {
        forcedUp = false;
        string? text = null;
        SyntaxTokenNode? last = null;
        for (int i = 0; i < mark.SlotCount; i++)
        {
            if (mark.GetChild(i) is not SyntaxTokenNode token)
                continue;
            if (token.Kind == SyntaxKind.StringLiteral && text == null)
            {
                var raw = token.Text;
                text = raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"'
                    ? raw[1..^1]
                    : raw;
            }
            last = token;
        }
        forcedUp = last is { Kind: SyntaxKind.Identifier, Text: "up" };
        return text;
    }

    /// <summary>
    /// True for the pedal music marks that anchor to the host note's column
    /// (the engage/release marks). Compound pedal marks like @ped.off arrive as
    /// MusicMarkSyntax note articulations and need this anchoring; other compound
    /// marks (rehearsal, etc.) are handled at the statement level instead.
    /// </summary>
    private static bool IsNoteAnchoredPedalMark(MusicMarkType type) =>
        type is MusicMarkType.SustainOn or MusicMarkType.SustainOff
             or MusicMarkType.SostenutoOn or MusicMarkType.SostenutoOff
             or MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff;

    /// <summary>
    /// True for the ottava music marks. The down forms (@ottava(bassa) /
    /// @quindicesima(bassa)) arrive as compound MusicMarkSyntax note articulations
    /// and, like pedals, must be anchored WITH the staff index so a grand staff's
    /// lower-staff 8vb is drawn and transposed on its own staff. (The up forms are
    /// plain @ottava / @quindicesima identifiers handled on the simple mark path.)
    /// </summary>
    private static bool IsOttavaMark(MusicMarkType type) =>
        type is MusicMarkType.OttavaUp or MusicMarkType.OttavaDown
             or MusicMarkType.QuindicesUp or MusicMarkType.QuindicesDown;

    /// <summary>
    /// Pairs trill spanner start/stop events into TrillSpannerItems.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/scheme-engravers.scm:47-85 start/stop pairing
    /// </remarks>
    private ImmutableArray<TrillSpannerItem> PairTrillSpannerEvents()
    {
        if (_trillSpannerEvents.Count == 0)
            return ImmutableArray<TrillSpannerItem>.Empty;

        var items = ImmutableArray.CreateBuilder<TrillSpannerItem>();
        (bool isStart, int measureIndex, int itemIndex, int sourcePosition, int staffIndex)? pendingStart = null;

        foreach (var evt in _trillSpannerEvents)
        {
            if (evt.isStart)
            {
                pendingStart = evt;
            }
            else if (pendingStart != null)
            {
                items.Add(new TrillSpannerItem(
                    pendingStart.Value.measureIndex,
                    pendingStart.Value.itemIndex,
                    evt.measureIndex,
                    evt.itemIndex,
                    pendingStart.Value.sourcePosition,
                    pendingStart.Value.staffIndex));
                pendingStart = null;
            }
        }

        return items.ToImmutable();
    }
}
