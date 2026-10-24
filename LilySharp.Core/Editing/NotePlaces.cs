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

using System;
using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>Where the score draws a note, a rest or a chord written at <see cref="Position"/>: the
/// bar number the page prints, the part, how far into the bar it starts (whole notes) and the
/// meter it is counted in.</summary>
public readonly record struct NotePlace(int Position, int Measure, string Part, Fraction Onset, TimeSignature Meter);

/// <summary>
/// The places of a document's notes, rests and chords and their written pitches, from one collect
/// of each score — what the caret's status line says of a note (owner's request 2026-10-24: the
/// bar and beat a note stands on, and what it sounds on a transposing part).
/// </summary>
/// <remarks>
/// The bar number is the one the page prints (<see cref="TodoIndex"/>'s rule: a leading pickup is
/// bar 0), and a pickup's first note stands as far into the bar as the bar is longer than the
/// pickup. The pitch is the DISPLAYED one the collector resolved (<see cref="MeasureCollector.PitchTrace"/>,
/// the <c>check --pitches</c> answer), keyed by the pitch's own position.
/// </remarks>
public sealed class NotePlaces
{
    private readonly List<NotePlace> _places;
    private readonly Dictionary<int, string> _written;

    private NotePlaces(List<NotePlace> places, Dictionary<int, string> written)
        => (_places, _written) = (places, written);

    /// <summary>A document's places; empty when it cannot be collected.</summary>
    public static NotePlaces Of(SyntaxTree tree)
    {
        var byPosition = new Dictionary<int, NotePlace>();
        var written = new Dictionary<int, string>();
        try
        {
            var all = RenderSpecParser.FindAll(tree);
            RenderSpec?[] specs = all.Count > 0 ? [.. all] : [null];
            foreach (var spec in specs)
            {
                var collector = new MeasureCollector();
                Collect(SvgGenerator.CollectScore(collector, tree, spec), byPosition);
                foreach (var e in collector.PitchTrace)
                    written.TryAdd(e.Position, e.Pitch);
            }
        }
        catch (Exception)
        {
            // A document the collector cannot walk has no places.
        }
        var places = byPosition.Values.ToList();
        places.Sort((a, b) => a.Position.CompareTo(b.Position));
        return new NotePlaces(places, written);
    }

    private static void Collect(MultiStaffScore score, Dictionary<int, NotePlace> byPosition)
    {
        var primary = score.PrimaryContentStaff.PrimaryVoice.Measures;
        if (primary.IsDefaultOrEmpty)
            return;
        var numbers = BarNumberEngraver.NumberMeasures(primary, primary[0].IsPickup ? -1 : 0);
        foreach (var (_, staff, _) in score.EnumerateStaves())
            foreach (var voice in staff.Voices)
            {
                var meter = score.TimeSignature;
                for (int m = 0; m < voice.Measures.Length && m < numbers.Length; m++)
                {
                    var measure = voice.Measures[m];
                    foreach (var change in measure.Items.OfType<TimeSignatureChangeItem>())
                        meter = change.NewTime;
                    // A pickup ends where its bar would: its first note stands as far in as
                    // the bar is longer than the pickup.
                    var onset = Fraction.Zero;
                    if (measure.IsPickup)
                    {
                        var length = Fraction.Zero;
                        foreach (var item in measure.Items)
                            length += Length(item);
                        var bar = new Fraction(meter.Beats, meter.BeatType);
                        if (bar > length)
                            onset = bar - length;
                    }
                    foreach (var item in measure.Items)
                    {
                        if (item is NoteItem or ChordItem or RestItem && item.SourcePosition >= 0)
                            byPosition.TryAdd(item.SourcePosition,
                                new NotePlace(item.SourcePosition, numbers[m], staff.PrimaryVoice.Name, onset, meter));
                        onset += Length(item);
                    }
                }
            }
    }

    private static Fraction Length(MusicItem item) => item is NoteItem or ChordItem or RestItem ? item.Duration : Fraction.Zero;

    /// <summary>The place of the note, rest or chord the score draws from a position in
    /// <paramref name="start"/>..<paramref name="end"/> (the written thing's span), or null.</summary>
    public NotePlace? At(int start, int end)
    {
        foreach (var place in _places)
            if (place.Position >= start && place.Position <= end)
                return place;
        return null;
    }

    /// <summary>The displayed pitch written at <paramref name="start"/>..<paramref name="end"/>
    /// (<c>D#4</c>), or null.</summary>
    public string? WrittenAt(int start, int end)
    {
        foreach (var (position, pitch) in _written)
            if (position >= start && position <= end)
                return pitch;
        return null;
    }
}
