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
/// not the same meter), and the numerator AS WRITTEN when it is additive (<c>3+2</c>, the
/// MusicXML's <c>&lt;beats&gt;</c> text) — <see cref="Length"/> is the bar it measures.</summary>
public readonly record struct Meter(int Beats, int BeatType, string? BeatsText = null)
{
    /// <summary>The length of one bar of this meter.</summary>
    public Fraction Length => new(Beats, BeatType);

    /// <summary>The same fraction — what a MIDI meter event carries and LilyPond's performer
    /// compares (the text is a spelling of the same bar).</summary>
    public bool SamePair(Meter other) => Beats == other.Beats && BeatType == other.BeatType;
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
/// and one copy (<c>R1*N</c> in the MIDI) had drifted. This type is REFACTOR_PLAN stage C: the
/// MIDI read it first (C2, whole), the MusicXML next (C3, the meter — its pickup is still its own,
/// see below), then the LilyPond twin (C4, whole; its section-head restores and the <c>\cadenzaOn</c>
/// bookkeeping stay the twin's), then the validator (C5, the meter; its pickup is a per-bar
/// reading, not a pending state, and its header registries keep their own rule — see
/// <see cref="SectionHeaders"/>). The page is not on it (owner's decision 2026-10-02, "iii": it is
/// the perf path and the authority the others are compared with; the differences below are decided
/// first).
/// <para>
/// ⚠️ SPELLINGS THAT STILL DIFFER between the outputs, recorded here so the move onto one type
/// does not paper over them (stage C5 decides, with nets): a second <c>partial</c> before the
/// pickup bar closes REPLACES the pending one here (the MIDI's rule), where the page keeps the
/// first (<c>MeasureBuilder.SetPartial</c>: <c>_partialRestore ??=</c>); <c>time none</c> leaves
/// the meter in force here (LilyPond's performer writes no event for a cadenza —
/// lily/time-signature-performer.cc:102-115), where the page freezes its clock
/// (<c>MeasureBuilder.FreezeOrThaw</c>); and the MusicXML spends a pickup by the duration
/// written into it, closing the implicit measure itself when the length is reached
/// (<c>MusicXmlExporter.MaybeClosePickup</c>, with <c>_pendingPickup</c> / <c>_pickupLength</c>),
/// where the MIDI and the page spend it at the first bar line that closes after it — so the
/// MusicXML does not read <see cref="Partial"/> yet.
/// </para>
/// </remarks>
public sealed class BarContext
{
    /// <summary>The score's home meter — the top-level <c>time</c> (<see cref="ScoreHomeMeter"/>),
    /// what a section that states no meter of its own reverts to. Set from that reading, or
    /// captured off the running meter once the top-level directives are walked
    /// (<see cref="CaptureHome"/>).</summary>
    public Meter HomeMeter { get; set; } = new(4, 4);

    /// <summary>Whether the score's home is <c>time none</c> (<see cref="SenzaMisura"/>).</summary>
    public bool HomeSenzaMisura { get; set; }

    /// <summary>The meter in force: the last metered <c>time</c> written or restored.
    /// Opens at 4/4, the language's default, until the music says otherwise.</summary>
    public Meter Meter { get; private set; } = new(4, 4);

    /// <summary>Whether a <c>time none</c> is in force: the meter above is then the last
    /// METERED one (what an empty bar is still worth, and what a DAW's grid still draws), and a
    /// reader that writes meters says "senza misura" instead of it (the MusicXML's
    /// <c>&lt;senza-misura&gt;</c>).</summary>
    public bool SenzaMisura { get; private set; }

    /// <summary>The pickup pending — a <c>partial</c> read (in the music, or a section header's)
    /// and not yet spent by a bar line — or null. Settable, because a section's pickup belongs
    /// to EVERY part's first bar and a walk that plays the parts one after another re-arms it
    /// per lane (MidiExporter.PlaySectionCore).</summary>
    public Fraction? Partial { get; set; }

    /// <summary>The meter half of the context as ONE value — what a nested walk inherits and
    /// hands back (the twin's StreamFrame carries it into a tuplet's or a phrase's exporter and,
    /// for a sequential body, out again). The pickup is not part of it: a body opens with none
    /// pending, and what it spends is its own.</summary>
    public readonly record struct MeterState(Meter Meter, bool SenzaMisura);

    /// <summary>The meter in force and whether <c>time none</c> is, as one value (<see cref="Restore"/>).</summary>
    public MeterState Save() => new(Meter, SenzaMisura);

    /// <summary>Puts back what <see cref="Save"/> took.</summary>
    public void Restore(MeterState state)
    {
        Meter = state.Meter;
        SenzaMisura = state.SenzaMisura;
    }

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
        SenzaMisura = time.IsSenzaMisura;
        if (time.IsSenzaMisura)
            return false;
        Meter = new(time.Beats, time.BeatType, time.BeatsText);
        return true;
    }

    /// <summary>A meter set outright — a row written as a part of its own opens at its
    /// section's (MusicXmlExporter.EmitRowAsItsOwnPart). Metered, so no <c>time none</c>.</summary>
    public void SetMeter(Meter meter)
    {
        Meter = meter;
        SenzaMisura = false;
    }

    /// <summary>A meter set outright that says nothing about <c>time none</c> — the validator's
    /// document-level <c>time</c> (MeasureValidator.SetTimeSignature), which never cleared its
    /// senza flag: a top-level <c>time none</c> leaves the document unmetered for every later
    /// top-level <c>time</c>. Kept as it was (stage C5 (iii) moves the state, not the rule);
    /// whether that is right is a separate decision, with its own net.</summary>
    public void SetMeterLeavingSenza(Meter meter) => Meter = meter;

    /// <summary>The running meter becomes the score's home — read once the top-level directives
    /// are walked and before any section is (MusicXmlExporter.Export's metadata pass).</summary>
    public void CaptureHome()
    {
        HomeMeter = Meter;
        HomeSenzaMisura = SenzaMisura;
    }

    /// <summary>A section that states no <c>time</c> of its own opens at the HOME meter — a
    /// mid-section change cannot leak into the next section, nor into the same section played
    /// again elsewhere by the form (<see cref="ScoreHomeMeter"/>; the page's
    /// <c>MeasureCollector.ProcessSectionPrologue</c>).</summary>
    public void RevertToHome()
    {
        Meter = HomeMeter;
        SenzaMisura = HomeSenzaMisura;
    }

    /// <summary>
    /// A section boundary, whole: the section's own header <c>time</c> if it states one
    /// (<see cref="SetTime"/> — a <c>time none</c> header is no meter at all and leaves the running
    /// one), else the HOME meter (<see cref="RevertToHome"/>). The header's <c>partial</c> becomes
    /// the pickup pending, and a pickup the previous section left pending ends with that section,
    /// as its bars did. Returns whether the meter's FRACTION moved (<see cref="Meter.SamePair"/>),
    /// so a boundary that changes nothing writes no event.
    /// </summary>
    public bool OpenSection(TimeSignatureSyntax? headerTime, PartialDeclarationSyntax? headerPartial)
    {
        var before = Meter;
        if (headerTime is null)
            RevertToHome();
        else
            SetTime(headerTime);
        Partial = headerPartial?.ToFraction();
        return !Meter.SamePair(before);
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
