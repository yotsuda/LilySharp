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
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

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
    /// <para>LILYPOND-REF: lily/figured-bass-engraver.cc - listen_bass_figure</para>
    /// <para>
    /// Written <c>@fig(6)</c> (single), <c>@fig(3 5)</c> (two figures), <c>@fig(6 s)</c>
    /// (with sharp). The parser normalises that argument run into the INTERNAL mark name
    /// <c>fig.6</c> / <c>fig.3.5</c> / <c>fig.6.s</c>, which is what
    /// <c>FiguredBassItem.ParseFigures</c> reads.
    /// </para>
    /// <para>
    /// ⚠️ The dotted form is the internal NAME, not the syntax. This remark used to say
    /// "Syntax: @fig.6 … @fig.6.4" and that spelling does not parse — measured
    /// 2026-08-15, `c4@fig.6` reports LYS0016 and produces no figure. A session read
    /// this line, believed it, and wrote a corpus claim on top of it (HANDOFF ▶ ⒯⑸).
    /// If you write a spelling in a remark, parse it first.
    /// </para>
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
    /// LILYPOND-REF: scm/scheme-engravers.scm:1513 - Current_chord_text_engraver
    /// Syntax: @chord.Cm7, @chord.Bb7, @chord.Am
    /// </remarks>
    private void CollectChordNames(SyntaxNode node, int measureIndex, int itemIndex)
    {
        var articulations = ArticulationsOf(node);

        foreach (var child in articulations)
        {
            if (child is not MusicMarkSyntax markSyntax)
                continue;

            // Bare '@chord' auto-derives the symbol from the notes it's on. On a
            // chord (or a << >> arpeggio — a broken chord names the same way) we
            // recognize it; on a single note there is nothing to derive (the
            // @chord() completion state), so it shows nothing.
            if (markSyntax.MarkName == "chord")
            {
                ChordStructure? derived = node switch
                {
                    ChordSyntax autoChord when TryNameChord(autoChord, out var s) => s,
                    ArpeggioSyntax arp when TryNameArpeggio(arp, out var s) => s,
                    // A `q` names what it repeats — derive from the original chord.
                    ChordRepetitionSyntax rep when ChordRepetitions.OriginalOf(rep) is { } orig
                        && TryNameChord(orig, out var s) => s,
                    _ => null,
                };
                if (derived != null)
                    _chordNameCollector.AddInline(derived.DisplayName, measureIndex, itemIndex,
                        markSyntax.Position, _currentStaffIndex, derived);
                continue;
            }

            var chordText = ChordNameItem.ParseChordName(markSyntax.MarkName, out var structure);
            if (chordText != null)
                _chordNameCollector.AddInline(
                    chordText, measureIndex, itemIndex, markSyntax.Position, _currentStaffIndex, structure);
        }
    }

    /// <summary>
    /// Derives a chord symbol from a chord's notes (root = first member; the
    /// remaining members' pitch classes give the quality). Returns false when the
    /// notes match no known quality. Named-pitch and scale-degree members are both
    /// resolved to pitch classes (degrees against the written key).
    /// </summary>
    private bool TryNameChord(ChordSyntax chord, out ChordStructure? structure)
    {
        structure = null;
        int keySharps = _meta.KeySharps - _octave.TransposeKeySharps(0); // written key
        // Inside a shifted phrase reference (Melody'(3)) the chord SOUNDS a
        // diatonic interval away from what is written — name what sounds.
        int diatonic = _octave.DiatonicShiftSteps;
        (int Step, int Alter) Sounding(int s, int a)
        {
            if (diatonic == 0) return (s, a);
            var (ss, sa, _) = Music.DiatonicShift.Apply(s, a, 4, diatonic, keySharps);
            return (ss, sa);
        }
        var pcs = new List<int>();
        int rootStep, rootAlter;

        if (chord.Root is { } rootPitch)
        {
            rootStep = GetPitchIndex(rootPitch.PitchName.ToLowerInvariant()[0]);
            rootAlter = rootPitch.AccidentalOffset;
            foreach (var p in chord.Pitches)
            {
                var (s, a) = Sounding(GetPitchIndex(p.PitchName.ToLowerInvariant()[0]), p.AccidentalOffset);
                pcs.Add(RelativeOctave.StepSemitoneOf(s) + a);
            }
        }
        else if (chord.Degrees.Any())
        {
            // Omitted root (<1 3 5>): the degrees stack on the key's tonic.
            rootStep = _ambientTonicValid ? _ambientTonicStep : 0;
            rootAlter = LilySharp.Core.Music.KeySpelling.Alteration(rootStep, keySharps);
        }
        else
        {
            return false;
        }

        foreach (var d in chord.Degrees)
        {
            var (ds, dalter, _) = ChordDegrees.Resolve(
                rootStep, 4, d.Number, d.Alteration, d.OctaveOffset, keySharps);
            var (s, a) = Sounding(ds, dalter);
            pcs.Add(RelativeOctave.StepSemitoneOf(s) + a);
        }

        // The chord's ROOT for naming is the FIRST DEGREE, not the key tonic that the
        // degrees are measured from: <2 4 6> in C major is D-F-A, i.e. Dm rooted on D,
        // not a C chord. (A named root already IS the chord root.)
        int recogStep = rootStep, recogAlter = rootAlter;
        if (chord.Root is null && chord.Degrees.Any())
        {
            var first = chord.Degrees.First();
            var (fs, fa, _) = ChordDegrees.Resolve(
                rootStep, 4, first.Number, first.Alteration, first.OctaveOffset, keySharps);
            recogStep = fs;
            recogAlter = fa;
        }
        (recogStep, recogAlter) = Sounding(recogStep, recogAlter);

        return ChordStructure.TryRecognize(recogStep, recogAlter, pcs, out structure);
    }

    /// <summary>
    /// Derives a chord symbol from a broken chord's members, the way
    /// <see cref="TryNameChord"/> does for a stacked chord: the naming root is the
    /// FIRST sounding member; every member (a nested chord contributes its pitches,
    /// a degree resolves against the anchor in the written key) gives a pitch class.
    /// The anchor for degrees = the first pitched member, or the key tonic when the
    /// group opens with degrees — the same rule the octave semantics use.
    /// </summary>
    private bool TryNameArpeggio(ArpeggioSyntax arp, out ChordStructure? structure)
    {
        structure = null;
        int keySharps = _meta.KeySharps - _octave.TransposeKeySharps(0); // written key
        var members = arp.Members
            .Where(m => m is PitchSyntax or ChordSyntax or ScaleDegreeSyntax).ToList();
        if (members.Count == 0)
            return false;

        var firstPitch = members
            .Select(m => m switch { PitchSyntax p => p, ChordSyntax c => c.Root, _ => null })
            .FirstOrDefault(p => p != null);
        int anchorStep;
        if (members[0] is ScaleDegreeSyntax || firstPitch is null)
            anchorStep = _ambientTonicValid ? _ambientTonicStep : 0;
        else
            anchorStep = GetPitchIndex(firstPitch.PitchName.ToLowerInvariant()[0]);

        var pcs = new List<int>();
        int recogStep = -1, recogAlter = 0;
        // Inside a shifted phrase reference (Melody'(3)) the group SOUNDS a
        // diatonic interval away from what is written — name what sounds.
        int diatonic = _octave.DiatonicShiftSteps;
        void Add(int step, int alter)
        {
            if (diatonic != 0)
                (step, alter, _) = Music.DiatonicShift.Apply(step, alter, 4, diatonic, keySharps);
            if (recogStep < 0) { recogStep = step; recogAlter = alter; }
            pcs.Add(RelativeOctave.StepSemitoneOf(step) + alter);
        }
        void AddPitch(PitchSyntax p) =>
            Add(GetPitchIndex(p.PitchName.ToLowerInvariant()[0]), p.AccidentalOffset);
        void AddDegree(ScaleDegreeSyntax d, int rootStep)
        {
            var (s, alter, _) = ChordDegrees.Resolve(
                rootStep, 4, d.Number, d.Alteration, d.OctaveOffset, keySharps);
            Add(s, alter);
        }

        foreach (var member in members)
        {
            switch (member)
            {
                case PitchSyntax p:
                    AddPitch(p);
                    break;
                case ChordSyntax c:
                    foreach (var p in c.Pitches) AddPitch(p);
                    int cRoot = c.Root is { } r
                        ? GetPitchIndex(r.PitchName.ToLowerInvariant()[0]) : anchorStep;
                    foreach (var d in c.Degrees) AddDegree(d, cRoot);
                    break;
                case ScaleDegreeSyntax d:
                    AddDegree(d, anchorStep);
                    break;
            }
        }
        return recogStep >= 0 && ChordStructure.TryRecognize(recogStep, recogAlter, pcs, out structure);
    }

    /// <summary>
    /// Detects @cross annotation on a note or chord for cross-staff rendering.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc:1497 Beam::is_cross_staff (calc_cross_staff @ 1509)
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
    private void CollectOverride(OverrideDeclarationSyntax node, int measureIndex, int itemIndex, bool isOnce, int? staffIndex)
    {
        string grobType = node.GrobName.Text;
        string propertyName = node.PropertyName.Text;
        // The token becomes a VALUE here — the one place that reads the spelling. Both
        // normalisations that used to live in this method (a quoted value stores its
        // CONTENT, and a folded negative number drops the interior whitespace it keeps
        // for round-tripping) moved into LysValue.FromToken, because they are properties
        // of the token rather than of any consumer. docs/VALUE_SITE_AUDIT.md §2.
        var value = LysValue.FromToken(node.ValueToken.Kind, node.ValueToken.Text);
        _grobOverrides.Add(new GrobOverride(grobType, propertyName, value, measureIndex, itemIndex, isOnce, staffIndex, _currentVoiceScope));
    }

    /// <summary>
    /// Collects a grob property revert from a RevertDeclarationSyntax.
    /// LILYPOND-REF: lily/context-property.cc (pop)
    /// </summary>
    private void CollectRevert(RevertDeclarationSyntax node, int measureIndex, int itemIndex, int? staffIndex)
    {
        string grobType = node.GrobName.Text;
        string propertyName = node.PropertyName.Text;
        _grobReverts.Add(new GrobRevert(grobType, propertyName, measureIndex, itemIndex, staffIndex, _currentVoiceScope));
    }

    /// <summary>
    /// Gets the feathered beam direction from a note's articulations.
    /// Returns 0 (none), 1 (right/accel), or -1 (left/rit).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc:773 get_property (me, "grow-direction") — that read is
    ///   Beam::print's, and it feathers the beam's own outline, not the stems.
    /// LILYPOND-REF: lily/beam.cc:1112-1157 Beam::calc_stem_y — where the feather direction
    ///   acts on stem length; the read that reaches it is the second one, at :1201 in
    ///   set_stem_lengths, handed over at :1221.
    ///   Beam grow-direction property @ define-grobs.scm; feather doc @ beam.cc:1597
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
        // A chord's scripts come from the whole-chord post-events AND from each
        // member (<c@staccato e@accent>), and the two are DIFFERENT GROBS in
        // LilyPond, not one list: a chord/note-level script is Script_engraver's,
        // a member script is New_fingering_engraver's (add_script) — they join the
        // same script column, but only Script_engraver acknowledges ties, which is
        // why the member flag rides along (see ArticulationItem.IsChordMember).
        // LILYPOND-REF: lily/script-engraver.cc (listen_articulation);
        //   lily/new-fingering-engraver.cc:109-110,144-157 add_script.
        var articulations = node switch
        {
            NoteSyntax note => note.Articulations.Select(a => (Node: a, IsMember: false)),
            ChordSyntax chord => chord.Articulations
                .Select(a => (Node: a, IsMember: false))
                .Concat(chord.Pitches.SelectMany(
                    p => p.Articulations.Select(a => (Node: a, IsMember: true)))),
            ChordRepetitionSyntax rep => rep.Articulations.Select(a => (Node: a, IsMember: false)),
            RestSyntax rest => rest.Articulations.Select(a => (Node: a, IsMember: false)),
            _ => Enumerable.Empty<(SyntaxNode Node, bool IsMember)>()
        };

        foreach (var (articulation, isChordMember) in articulations)
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
                    // LILYPOND-REF: scm/script.scm:128 fermata (direction . UP);
                    // ornaments (trill/prall/mordent/turn) carry their own direction in
                    // the same script-alist (scm/script.scm).
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
                    bool directionForced = false;
                    if (articulationSyntax.ForcedAbove is bool forcedAbove)
                    {
                        isAbove = forcedAbove;
                        directionForced = true;
                    }

                    _articulations.Add(new ArticulationItem(type, measureIndex, itemIndex, isAbove,
                        articulationSyntax.Position, _currentStaffIndex)
                    {
                        DirectionForced = directionForced,
                        VoiceIndex = _currentVoiceIndex,
                        IsChordMember = isChordMember,
                    });
                }
                else
                {
                    // Check for trill spanner start/stop
                    // LILYPOND-REF: scm/scheme-engravers.scm — \startTrillSpan / \stopTrillSpan
                    var nameText = articulationSyntax.NameToken.Text;
                    var nameLower = nameText.ToLowerInvariant();
                    if (nameLower == "starttrillspan")
                    {
                        // .up/.down forces the spanner's direction — LilyPond's
                        // ^\startTrillSpan / _\startTrillSpan, which the engraver sets
                        // on the grob over the voice default. Until 2026-08-09 the
                        // suffix was accepted and silently dropped (trillsdir-probe).
                        // LILYPOND-REF: scm/scheme-engravers.scm:1818-1820 Trill_spanner_engraver.
                        int forcedDir = articulationSyntax.ForcedAbove switch
                        {
                            true => 1, false => -1, null => 0,
                        };
                        _trillSpannerEvents.Add((true, measureIndex, itemIndex, articulationSyntax.Position, _currentStaffIndex, _currentVoiceIndex, forcedDir));
                    }
                    else if (nameLower == "stoptrillspan")
                    {
                        _trillSpannerEvents.Add((false, measureIndex, itemIndex, articulationSyntax.Position, _currentStaffIndex, _currentVoiceIndex, 0));
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
                            articulationSyntax.Position, _currentStaffIndex)
                        { VoiceIndex = _currentVoiceIndex, IsChordMember = isChordMember });
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
                // Compound mark syntax. The trill spanner is NOT here: it is
                // @startTrillSpan / @stopTrillSpan, one word each, exactly as in
                // LilyPond — the '@trillSpan(start)' spelling was a second way to
                // say the same thing and was dropped.
                var markName = markSyntax.MarkName.ToLowerInvariant();
                if (Semantics.AnnotationValues.Pluck(markSyntax) is { } pluckLetter)
                {
                    // p-i-m-a right-hand fingering, printed BELOW the note.
                    _articulations.Add(new ArticulationItem(
                        ArticulationType.Pluck, measureIndex, itemIndex, false,
                        markSyntax.Position, _currentStaffIndex)
                    { PluckLetter = pluckLetter, VoiceIndex = _currentVoiceIndex });
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
                        { FrameSpec = spec, VoiceIndex = _currentVoiceIndex });
                }
                else if (Semantics.AnnotationValues.Bend(markSyntax) is { } semitones)
                {
                    // @bend(full|half|N) — guitar bend-up, N in semitones.
                    _articulations.Add(new ArticulationItem(
                        ArticulationType.Bend, measureIndex, itemIndex, true,
                        markSyntax.Position, _currentStaffIndex)
                    { BendSemitones = semitones, VoiceIndex = _currentVoiceIndex });
                }
                else if (markSyntax.Name.Equals("notehead", StringComparison.OrdinalIgnoreCase)
                         && markSyntax.HasArgumentList)
                {
                    // Consumed by ExtractNoteheadStyle at item creation — not a
                    // printed mark. The STYLE is not read here, so this arm asks only
                    // what was written: an unrecognised one draws an ordinary head
                    // (and the validator has already warned about it).
                }
                else if (Semantics.AnnotationValues.Finger(markSyntax) is { } finger)
                {
                    // LILYPOND-REF: lily/fingering-engraver.cc — finger event attaches to
                    // the host note. Keyed by the note's source position.
                    _fingeringByPosition[node.Position] = finger;
                }
                else if (markSyntax.Name.Equals("text", StringComparison.OrdinalIgnoreCase))
                {
                    // @text("dolce")[.up/.down] — free expressive text on the host
                    // note. Rides the DynamicText pipeline as expressive text: it
                    // is NOT a dynamic level, so hairpins ignore it and MIDI is
                    // untouched. LILYPOND-REF: TextScript (LP's c^"text"/c_"text"),
                    // direction DOWN by default.
                    if (Semantics.AnnotationValues.Text(markSyntax) is { } freeText)
                        _dynamics.Add(new DynamicItem(
                            freeText, measureIndex, itemIndex, markSyntax.Position, _currentStaffIndex)
                        {
                            IsAbove = markSyntax.ForcedAbove == true,
                            VoiceIndex = _currentVoiceIndex,
                        });
                }
                else if (MusicMarkItem.ParseMarkName(markSyntax.MarkName) is { } compoundMark
                         && (IsNoteAnchoredPedalMark(compoundMark) || IsOttavaMark(compoundMark)))
                {
                    // A compound OTTAVA mark (the down forms @ottava(bassa) /
                    // @quindicesima(bassa)) written ON a note. The pedals reach this
                    // predicate too, but they are plain one-word names now
                    // (@sustainOff), so they arrive through the articulation path
                    // above. Like the plain @ottava, anchor it to the
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
    /// True for the pedal music marks that anchor to the host note's column
    /// (the engage/release marks, @sustainOn … @treCorde). They are plain
    /// one-word names, so they arrive as ordinary note articulations and need this
    /// anchoring; compound marks (rehearsal, etc.) are handled at the statement
    /// level instead.
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
    /// LILYPOND-REF: scm/scheme-engravers.scm:1798 Trill_spanner_engraver
    /// </remarks>
    private ImmutableArray<TrillSpannerItem> PairTrillSpannerEvents(int measureCount)
    {
        if (_trillSpannerEvents.Count == 0)
            return ImmutableArray<TrillSpannerItem>.Empty;

        var items = ImmutableArray.CreateBuilder<TrillSpannerItem>();
        (bool isStart, int measureIndex, int itemIndex, int sourcePosition, int staffIndex,
            int voiceIndex, int forcedDir)? pendingStart = null;

        foreach (var evt in _trillSpannerEvents)
        {
            if (evt.isStart)
            {
                // A NEW START while a spanner runs ENDS the running one at the new
                // start's own column — LilyPond's `ender = (or stop-event start-event)`:
                // the ended trill's right bound becomes the column the new one begins
                // on. Until 2026-08-09 the pending start was simply overwritten and the
                // running trill vanished without a mark (trill-spanner-direction.ly
                // chains four starts with no stop and lost the first three).
                // LILYPOND-REF: scm/scheme-engravers.scm:1809-1814 Trill_spanner_engraver
                //   process-music — the ender path; :1833-1837 note-column-interface
                //   acknowledger — the ended trill's right bound is the current column.
                if (pendingStart != null)
                    items.Add(new TrillSpannerItem(
                        pendingStart.Value.measureIndex,
                        pendingStart.Value.itemIndex,
                        evt.measureIndex,
                        evt.itemIndex,
                        pendingStart.Value.sourcePosition,
                        pendingStart.Value.staffIndex,
                        pendingStart.Value.voiceIndex,
                        Direction: pendingStart.Value.forcedDir));
                pendingStart = evt;
            }
            else if (pendingStart != null)
            {
                // The START event's voice owns the spanner: LilyPond's engraver lives in
                // that Voice context and makes the grob there (scheme-engravers.scm:1816),
                // so its supports and its left bound are that voice's columns.
                items.Add(new TrillSpannerItem(
                    pendingStart.Value.measureIndex,
                    pendingStart.Value.itemIndex,
                    evt.measureIndex,
                    evt.itemIndex,
                    pendingStart.Value.sourcePosition,
                    pendingStart.Value.staffIndex,
                    pendingStart.Value.voiceIndex,
                    Direction: pendingStart.Value.forcedDir));
                pendingStart = null;
            }
        }

        // A start with no @stopTrillSpan runs to the END OF THE SCORE (it used to
        // be dropped without a mark). Encoded as a virtual stop at item 0 of the
        // measure PAST the last one, it rides the existing to-barline branch —
        // the engraver's endsOnMeasureStart allows EndMeasureIndex ==
        // measureLayouts.Length exactly for this — so the line stops short of the
        // final barline by the same term a written stop-at-bar does (LP measured:
        // both gaps 1.75, trillimpl/trillbar twins).
        // LILYPOND-REF: scm/scheme-engravers.scm:1798 Trill_spanner_engraver —
        //   finalize ends an open spanner at the piece's end column.
        if (pendingStart != null)
            items.Add(new TrillSpannerItem(
                pendingStart.Value.measureIndex,
                pendingStart.Value.itemIndex,
                measureCount,
                0,
                pendingStart.Value.sourcePosition,
                pendingStart.Value.staffIndex,
                pendingStart.Value.voiceIndex,
                Direction: pendingStart.Value.forcedDir));

        return items.ToImmutable();
    }
}
