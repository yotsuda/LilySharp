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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Packs the accidentals of ALL the voices standing on one staff column into a single
/// accidental column, and bakes the resulting per-note X into the model
/// (<see cref="NoteItem.AccidentalX"/> / <see cref="ChordNoteInfo.AccidentalX"/>).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/accidental-placement.cc:479-518 Accidental_placement::calc_positioning_done
///   — one AccidentalPlacement grob per staff moment. <c>extract_heads_and_stems</c> (:303-355)
///   walks the note columns of EVERY accidental it holds and takes their heads at their real
///   (collision-shifted) X in the common refpoint, <c>build_heads_skyline</c> (:375-385) makes
///   one reference skyline out of all of them, and <c>position_apes</c> (:391-438) stacks the
///   whole set right-to-left. Nothing in that path is per-voice.
/// <para>
/// ⚠️ THE ACCIDENTALS DO NOT RIDE THE NOTE-COLLISION SHIFT. <c>position_apes</c> translates
/// each accidental relative to the placement grob, which is not inside the shifted note column
/// — so the answer is in the COLUMN's frame. MEASURED (LilyPond 2.26.0,
/// audit/lp-geometry/probes/cross-voice-accidental.ly):
/// <code>
///   XVA  &lt;&lt; { aes' } \\ { g' } &gt;&gt;   heads 9.059735 / 10.363935 · ONE flat at 7.909735
///   XVB  &lt;&lt; { a' } \\ { ges' } &gt;&gt;   heads 9.059735 / 10.363935 · ONE flat at 7.909735
///   XVC  &lt;&lt; { aes' } \\ { ges' } &gt;&gt; heads 10.126351 / 11.430551 · flats 8.976351 / 7.909736
///   XVD  &lt;ges' aes'&gt; (one voice)   heads 10.095351 / 11.334551 · flats 8.976351 / 7.909736
/// </code>
/// XVA and XVB put the SAME flat at the SAME X whichever voice carries it — 0.35 left of the
/// LEFTMOST head, not of its own. XVC and XVD agree to fourteen digits: a staff column's
/// accidentals are packed exactly as a chord's are.
/// </para>
/// <para>
/// This runs at collect time, next to <see cref="VoiceDefaults"/>'s stem forcing and for the
/// same reason: the reservation (SpacingRules), the skylines and the renderer must all see one
/// answer, and spacing runs long before the layout knows about voice collisions. Everything
/// that reserves already measures from the column, so it reads the baked value bare; only
/// <c>SharedRenderer</c> draws at the shifted X and subtracts the shift back off.
/// </para>
/// </remarks>
internal static class StaffAccidentalColumns
{
    /// <summary>
    /// Returns <paramref name="voices"/> with every column that carries more than one voice
    /// re-solved as one accidental column. A column with a single voice on it is left alone:
    /// the per-item solve every consumer already runs IS that column's solve, so single-voice
    /// music comes through byte-identical.
    /// </summary>
    public static ImmutableArray<Voice> Resolve(ImmutableArray<Voice> voices)
    {
        if (voices.Length <= 1)
            return voices;

        var columns = new VoiceCollector().Collect(voices);
        var collision = new NoteCollision();
        var placement = new AccidentalPlacement();

        // (measureIndex, voiceId, itemIndex, noteIndex) -> packed ink-left X, column frame.
        var packed = new Dictionary<(int Measure, int Voice, int Item, int Note), double>();

        foreach (var column in columns)
        {
            if (column.Entries.Length <= 1)
                continue;
            if (!column.Entries.Any(HasAccidental))
                continue;
            // ⚠️ LILYSHARP-OWN GATE, and a DIVERGENCE: LilyPond packs a cue accidental into
            // the staff's column like any other, each ape carrying skylines from its own
            // font. CalculatePositions reads ONE font for the whole call, so a column mixing
            // cue and full-size grobs cannot be expressed and is left to the per-item path —
            // where the reported overlap comes back. Closing it means giving
            // CalculatePositions a font per note.
            // ⚠️ NOT reachable today, and MEASURED rather than assumed (2026-08-05): the only
            // spelling that would produce such a column, `voice { cue { … } } { … }`, is
            // broken upstream — the cue branch becomes its own measure and draws at full
            // size, while `lysc ly` emits the correct two-measure
            // `<< { \new CueVoice { … } } \\ { … } >>`. Ticketed in HANDOFF §2 A, and that
            // ticket has to close before this gate can be measured against anything.
            if (column.Entries.Select(IsCue).Distinct().Count() != 1)
                continue;

            // ONE FONT ANSWERS BOTH HALVES. The stacking below read CueFont — the design
            // font-size −4 selects — while the within-chord reversal beside it read the
            // twenty's box times CueScale, so the same column packed its accidentals against
            // heads it had placed 0.006248 away from where it drew them
            // (ChordHeadPositioning, measured against 2.26.0).
            bool isCue = IsCue(column.Entries[0]);
            var font = isCue ? EngravingDefaults.CueFont : (GlyphMetrics.DesignMetrics?)null;

            var offsets = collision.CalculateVoiceOffsets(column);

            // The union of every head on the column, each at the X it is actually drawn at:
            // the voice's collision shift plus, within a chord, its reversed-head offset.
            // LILYPOND-REF: accidental-placement.cc:375-385 build_heads_skyline.
            var notes = new List<ChordNoteInfo>();
            var headOffsets = new List<double>();
            var slots = new List<(int Voice, int Item, int Note)>();

            foreach (var entry in column.Entries)
            {
                double voiceX = 0;
                foreach (var o in offsets)
                    if (o.VoiceId == entry.VoiceId && o.ItemIndex == entry.ItemIndex)
                    {
                        voiceX = o.XOffset;
                        break;
                    }

                switch (entry.Item)
                {
                    case NoteItem note:
                        notes.Add(new ChordNoteInfo(
                            note.StaffPosition, note.Accidental,
                            note.NeedsLedgerLines, note.IsCourtesy));
                        headOffsets.Add(voiceX);
                        slots.Add((entry.VoiceId, entry.ItemIndex, 0));
                        break;

                    case ChordItem chord:
                        int noteValue = LayoutUtilities.GetNoteValueFromFraction(chord.BaseDuration);
                        bool stemUp = entry.ForcedStemUp ?? chord.StemUp;
                        var within = ChordHeadPositioning.CalculateOffsets(
                            chord.Notes, stemUp, noteValue, font);
                        for (int i = 0; i < chord.Notes.Length; i++)
                        {
                            notes.Add(chord.Notes[i]);
                            headOffsets.Add(voiceX + within[i]);
                            slots.Add((entry.VoiceId, entry.ItemIndex, i));
                        }
                        break;
                }
            }

            if (notes.Count == 0)
                continue;

            var layouts = placement.CalculatePositions(notes, headOffsets, font, font);
            if (layouts.Length == 0)
                continue;

            // position_apes gives one X per APE, and an ape is (note name, alteration) — two
            // notes that share it share the column, whatever the octave or the voice. So the
            // answer is looked up by what the ape is keyed on, not by which note asked.
            // LILYPOND-REF: accidental-placement.cc:55-81 add_accidental.
            var byKey = new Dictionary<(int Position, string Accidental, bool Courtesy), double>();
            foreach (var l in layouts)
                byKey.TryAdd((l.StaffPosition, l.Accidental, l.IsCourtesy), l.XOffset);

            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i].Accidental is not { } acc)
                    continue;
                if (!byKey.TryGetValue(
                        (notes[i].StaffPosition, acc, notes[i].IsCourtesy), out double x))
                    continue;
                packed[(column.MeasureIndex, slots[i].Voice, slots[i].Item, slots[i].Note)] = x;
            }
        }

        return packed.Count == 0 ? voices : Bake(voices, packed);
    }

    private static bool HasAccidental(VoiceEntry entry) => entry.Item switch
    {
        NoteItem note => note.Accidental != null,
        ChordItem chord => chord.Notes.Any(n => n.Accidental != null),
        _ => false,
    };

    private static bool IsCue(VoiceEntry entry) => entry.Item switch
    {
        NoteItem note => note.IsCue,
        ChordItem chord => chord.IsCue,
        _ => false,
    };

    private static ImmutableArray<Voice> Bake(
        ImmutableArray<Voice> voices,
        Dictionary<(int Measure, int Voice, int Item, int Note), double> packed)
    {
        var rebuilt = voices.ToBuilder();
        for (int vi = 0; vi < voices.Length; vi++)
        {
            int voiceId = vi + 1;
            var measures = voices[vi].Measures.ToBuilder();
            bool voiceChanged = false;

            for (int mi = 0; mi < measures.Count; mi++)
            {
                var measure = measures[mi];
                var items = measure.Items.ToBuilder();
                bool measureChanged = false;

                for (int ii = 0; ii < items.Count; ii++)
                {
                    switch (items[ii])
                    {
                        case NoteItem note when packed.TryGetValue((mi, voiceId, ii, 0), out double x):
                            items[ii] = note with { AccidentalX = x };
                            measureChanged = true;
                            break;

                        case ChordItem chord:
                        {
                            var notes = chord.Notes.ToBuilder();
                            bool chordChanged = false;
                            for (int ni = 0; ni < notes.Count; ni++)
                                if (packed.TryGetValue((mi, voiceId, ii, ni), out double cx))
                                {
                                    notes[ni] = notes[ni] with { AccidentalX = cx };
                                    chordChanged = true;
                                }
                            if (chordChanged)
                            {
                                items[ii] = chord with { Notes = notes.ToImmutable() };
                                measureChanged = true;
                            }
                            break;
                        }
                    }
                }

                if (!measureChanged)
                    continue;

                measures[mi] = new Measure(
                    items.ToImmutable(),
                    measure.StartBarline, measure.EndBarline, measure.SectionLabel,
                    measure.SourceStart, measure.SourceEnd,
                    hasBreakAfter: measure.HasBreakAfter,
                    lineBreakPermission: measure.LineBreakPermission,
                    breakPenalty: measure.BreakPenalty,
                    pageBreakPermission: measure.PageBreakPermission,
                    pageTurnPermission: measure.PageTurnPermission,
                    sectionLabelPosition: measure.SectionLabelPosition,
                    isPickup: measure.IsPickup);
                voiceChanged = true;
            }

            if (voiceChanged)
                rebuilt[vi] = voices[vi] with { Measures = measures.ToImmutable() };
        }
        return rebuilt.ToImmutable();
    }
}
