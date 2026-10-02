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

using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>A written meter: the pair <c>\time</c> takes, kept as the pair (4/4 and 2/2 are
/// not the same meter) — <see cref="Length"/> is the bar it measures.</summary>
public readonly record struct Meter(int Beats, int BeatType)
{
    /// <summary>The length of one bar of this meter.</summary>
    public Fraction Length => new(Beats, BeatType);
}

/// <summary>
/// The bar's context as a walk advances through the music — the meter in force, the score's
/// home meter it reverts to, and the pickup pending — with the rules that move them: a
/// <c>time</c> (<see cref="SetTime"/>), a section boundary (<see cref="OpenSection"/>), a
/// <c>partial</c> (<see cref="SetPartial"/>) and the bar line that spends it
/// (<see cref="SpendPartial"/>). What an empty bar, a bare <c>R</c> or a padding bar is worth
/// is read here (<see cref="BarLength"/>, <see cref="MeterLength"/>), not re-derived.
/// </summary>
/// <remarks>
/// ⚠️ THE RULES ARE THE PAGE'S (<c>MeasureBuilder</c>: <c>SetPartial</c> /
/// <c>RestorePartialIfPending</c> / <c>EmitEmptyMeasure</c>'s "the meter in force";
/// <c>MeasureCollector.ProcessSectionPrologue</c>: the section revert, <see cref="ScoreHomeMeter"/>)
/// and every output used to keep its own copy of them in its own fields — the MIDI's
/// <c>_timeNumerator / _timeDenominator / _partial / _homeTimeBeats</c>, the MusicXML's, the
/// twin's — so a rule added to one (the bare <c>R</c>, 2026-10-02) had to be added to six places,
/// and one copy (<c>R1*N</c> in the MIDI) had drifted. This type is REFACTOR_PLAN stage C2: the
/// MIDI reads it first; the MusicXML, the twin, the validator and the page follow, one at a time,
/// each checked by its own full-corpus byte comparison.
/// <para>
/// ⚠️ TWO SPELLINGS STILL DIFFER between the MIDI (which this type follows for now) and the page,
/// recorded here so the move onto one type does not paper over them (stage C5 decides, with nets):
/// a second <c>partial</c> before the pickup bar closes REPLACES the pending one here, where the
/// page keeps the first (<c>MeasureBuilder.SetPartial</c>: <c>_partialRestore ??=</c>); and
/// <c>time none</c> leaves the meter in force here (LilyPond's performer writes no event for a
/// cadenza — lily/time-signature-performer.cc:102-115), where the page freezes its clock
/// (<c>MeasureBuilder.FreezeOrThaw</c>).
/// </para>
/// </remarks>
public sealed class BarContext
{
    /// <summary>The score's home meter — the top-level <c>time</c> (<see cref="ScoreHomeMeter"/>),
    /// what a section that states no meter of its own reverts to.</summary>
    public Meter HomeMeter { get; set; } = new(4, 4);

    /// <summary>The meter in force: the last metered <c>time</c> written or restored.
    /// Opens at 4/4, the language's default, until the music says otherwise.</summary>
    public Meter Meter { get; private set; } = new(4, 4);

    /// <summary>The pickup pending — a <c>partial</c> read (in the music, or a section header's)
    /// and not yet spent by a bar line — or null. Settable, because a section's pickup belongs
    /// to EVERY part's first bar and a walk that plays the parts one after another re-arms it
    /// per lane (MidiExporter.PlaySectionCore).</summary>
    public Fraction? Partial { get; set; }

    /// <summary>One bar of the meter in force — what a padding bar is worth.</summary>
    public Fraction MeterLength => Meter.Length;

    /// <summary>What the bar in progress is worth: the pickup when one is pending, else one bar
    /// of the meter — the length of an empty <c>| |</c> bar, of a bare <c>R</c>, of a chord row's
    /// first bar (the same length the page's <c>MeasureBuilder.EmitEmptyMeasure</c> gives its
    /// spacer, so the walks agree on what a gap is worth).</summary>
    public Fraction BarLength => Partial ?? Meter.Length;

    /// <summary>
    /// A <c>time</c> in the music. A metered one becomes the meter in force (true: the caller
    /// writes whatever event its output has for it). <c>time none</c> changes nothing and asks
    /// for no event (false): LilyPond's \cadenzaOn sets Timing.timing, not timeSignature, and the
    /// performer emits only on a \time event or a changed fraction
    /// (LILYPOND-REF: lily/time-signature-performer.cc:102-115 Time_signature_performer::process_music).
    /// </summary>
    public bool SetTime(TimeSignatureSyntax time)
    {
        if (time.IsSenzaMisura)
            return false;
        Meter = new(time.Beats, time.BeatType);
        return true;
    }

    /// <summary>
    /// A section boundary: the section's own header <c>time</c> if it states one, else the HOME
    /// meter (a mid-section change cannot leak into the next section — <see cref="ScoreHomeMeter"/>);
    /// a <c>time none</c> header is no meter at all and leaves the running one. The header's
    /// <c>partial</c> becomes the pickup pending, and a pickup the previous section left pending
    /// ends with that section, as its bars did. Returns whether the meter MOVED, so a boundary
    /// that changes nothing writes no event.
    /// </summary>
    public bool OpenSection(TimeSignatureSyntax? headerTime, PartialDeclarationSyntax? headerPartial)
    {
        var next = headerTime is null ? HomeMeter
            : headerTime.IsSenzaMisura ? Meter
            : new Meter(headerTime.Beats, headerTime.BeatType);
        bool moved = next != Meter;
        Meter = next;
        Partial = headerPartial?.ToFraction();
        return moved;
    }

    /// <summary>A <c>partial</c> in the music: the bar in progress is a pickup of this length
    /// until it closes. The clock needs nothing from it — notes take their written time either
    /// way — but an empty bar inside the pickup is worth the pickup, not the meter.
    /// LILYPOND-REF: ly/music-functions-init.ly:1697-1705 partial = context-spec-music 'Timing
    ///   — "adjust the measure position to end the current measure at dur past the point of use".</summary>
    public void SetPartial(Fraction length) => Partial = length;

    /// <summary>A bar closed: a pending pickup is SPENT and the meter is back in force
    /// (the page's <c>MeasureBuilder.RestorePartialIfPending</c> at every measure close).</summary>
    public void SpendPartial() => Partial = null;
}
