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
/// <c>time</c> (<see cref="SetTime(TimeSignatureSyntax)"/>), a section boundary (<see cref="OpenSection"/>), a
/// <c>partial</c> (<see cref="SetPartial"/>) and the bar line that spends it
/// (<see cref="SpendPartial"/>). What an empty bar, a bare <c>R</c> or a padding bar is worth
/// is read here (<see cref="BarLength"/>, <see cref="MeterLength"/>), not re-derived.
/// </summary>
/// <remarks>
/// ⚠️ THE RULES ARE THE PAGE'S (<c>MeasureBuilder</c>: <c>SetPartial</c> /
/// <c>SpendPartial</c> at every measure close / <c>EmitEmptyMeasure</c>'s "the meter in force" /
/// <c>RevertMeterToHome</c>: the section revert, <see cref="ScoreHomeMeter"/>)
/// and every output used to keep its own copy of them in its own fields — the MIDI's
/// <c>_timeNumerator / _timeDenominator / _partial / _homeTimeBeats</c>, the MusicXML's, the
/// twin's — so a rule added to one (the bare <c>R</c>, 2026-10-02) had to be added to six places,
/// and one copy (<c>R1*N</c> in the MIDI) had drifted. This type is REFACTOR_PLAN stage C: the
/// MIDI read it first (C2, whole), the MusicXML next (C3, the meter — its pickup is still its own,
/// see below), then the LilyPond twin (C4, whole; its section-head restores and the <c>\cadenzaOn</c>
/// bookkeeping stay the twin's), then the validator (C5 (iii), the meter; its pickup is a per-bar
/// reading, not a pending state, and its header registries keep their own rule — see
/// <see cref="SectionHeaders"/>), and last the page itself (C5, whole: the meter, <c>time none</c>
/// and the pickup; the clock it freezes under <c>time none</c> is the page's own state,
/// <c>MeasureBuilder._frozenPosition</c>; C6: the home it reverts to at a section boundary,
/// armed per voice from the collector's score-level meter). Every reader is on it now.
/// <para>
/// The spellings stage C found to differ, all settled by the owner's decisions (p750–p755)
/// and recorded here so the move onto one type does not paper over them: the MusicXML spent a
/// pickup by the duration written into it alone — closing the implicit measure itself when the
/// length was reached, and running ON across a bar line written before that — where the MIDI,
/// the twin and the page spend it at the first bar line that closes after it; since p755 (A2,
/// 2026-10-03) it reads <see cref="Partial"/> and its bar line spends it too, keeping the
/// auto-close at the declared length (the page's <c>MeasureBuilder.AddItem</c> has the same;
/// <c>MusicXmlExporter.MaybeClosePickup</c>). A <c>time</c> while a pickup is
/// pending moves the meter and leaves the pickup, in every output (<see cref="SetTime(Meter?, bool)"/>;
/// the page re-armed the pickup until p751); a top-level <c>time none</c> is undone by a later
/// top-level <c>time</c> in the validator as everywhere else (p751); a section that states no
/// meter reverts to the home in all of them, and all of them write that move for a changed PAIR
/// (<see cref="OpenSection"/>, <see cref="LeftHome"/>; the page asked the bar's reduced length
/// until p750). A second <c>partial</c> before the pickup bar closes REPLACES the pending one in all of them
/// (until C5 this type recorded the page as keeping the first: its <c>_partialRestore ??=</c>
/// parked the METER to restore, not the first pickup — the pickup field was overwritten). Under
/// <c>time none</c> all of them keep the last metered meter in force (LilyPond's performer writes
/// no event for a cadenza — lily/time-signature-performer.cc:102-115); only the page has a clock
/// to freeze.
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
    public Meter Meter
    {
        get => _meter;
        private set { _meter = value; _meterLength = value.Length; }
    }
    private Meter _meter = new(4, 4);
    // One bar of _meter, kept beside it: Fraction's constructor reduces (a GCD), and the page
    // asks what the bar is worth on every item it adds (MeasureBuilder.AddItem's auto-complete),
    // where it used to read one field of its own — so the answer stays one field read.
    private Fraction _meterLength = new(4, 4);

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
    public Fraction MeterLength => _meterLength;

    /// <summary>What the bar in progress is worth: the pickup when one is pending, else one bar
    /// of the meter — the length of an empty <c>| |</c> bar, of a bare <c>R</c>, of a chord row's
    /// first bar, and the length the page auto-completes a bar at and gives its placeholder
    /// spacer (<c>MeasureBuilder.AddItem</c>, <c>EmitEmptyMeasure</c>), so the walks agree on
    /// what a gap is worth.</summary>
    public Fraction BarLength => Partial ?? _meterLength;

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

    /// <summary>The page's meter write (<c>MeasureBuilder</c>: its opening meter, a section
    /// head's or reset's, a <c>time</c> in the music) — the same move as
    /// <see cref="SetTime(TimeSignatureSyntax)"/> from the page's model type: a pending pickup
    /// is untouched (a <c>partial</c> says how long the bar in progress is, a <c>time</c> says
    /// how long the bars after it are — two different things, and the one does not rewrite the
    /// other). <paramref name="meter"/> null is a <c>time none</c> change item, which carries no
    /// meter; a header's or a reset's <c>time none</c> carries the last metered pair, the
    /// placeholder spacer's length.</summary>
    /// <remarks>
    /// Until 2026-10-02 (p751, owner's decision: musical validity decides the semantics) the page
    /// re-armed the PICKUP to a whole bar of the new meter and kept the meter from before the
    /// <c>partial</c> — the one-field spelling it had before stage C5 (<c>_timeSignature</c> was
    /// both the pickup and the meter) made it so, and no book or test observed it.
    /// </remarks>
    public void SetTime(Meter? meter, bool senzaMisura)
    {
        if (meter is { } m)
            Meter = m;
        SenzaMisura = senzaMisura;
    }

    /// <summary>The running meter becomes the score's home — read once the top-level directives
    /// are walked and before any section is (MusicXmlExporter.Export's metadata pass).</summary>
    public void CaptureHome()
    {
        HomeMeter = Meter;
        HomeSenzaMisura = SenzaMisura;
    }

    /// <summary>Whether the section before left the home — what a boundary that states no meter
    /// RESTATES (the page's <c>MeasureBuilder.RevertMeterToHome</c>; the twin's <c>\time</c> at
    /// the boundary, LilyPondExporter.Form): the PAIR and <c>time none</c>. 2/2 after a 4/4 home
    /// is a change — the twin writes <c>\time 4/4</c>, and LilyPond prints a TimeSignature for
    /// every \time event because its engraver compares the spec by identity
    /// (LILYPOND-REF: lily/time-signature-engraver.cc:99-105 process_music, scm_is_eq on last_spec_).
    /// A pickup pending at the boundary is not a meter and does not count. The MIDI asks the
    /// same pair (<see cref="OpenSection"/>, <see cref="Meter.SamePair"/>).</summary>
    /// <remarks>
    /// Until 2026-10-02 the page asked the bar's reduced LENGTH instead (<c>BarLength</c>
    /// against the home's): 2/2 → 4/4 drew nothing and stayed 2/2 where the twin restated 4/4,
    /// and a pickup still pending at the boundary (<c>section Body_1 { partial 2 }</c> whose body
    /// is <c>r8</c> with no bar line — `Locked out of Heaven`) read 1/2 ≠ 1, drew a 4/4 of the
    /// same meter and re-armed the pickup to a whole bar (the page's own <c>time</c> rule, gone in p751).
    /// Owner's GO 2026-10-02 (p750): the page asks the pair like everyone else.
    /// </remarks>
    public bool LeftHome => !Meter.SamePair(HomeMeter) || SenzaMisura != HomeSenzaMisura;

    /// <summary>A section that states no <c>time</c> of its own opens at the HOME meter — a
    /// mid-section change cannot leak into the next section, nor into the same section played
    /// again elsewhere by the form (<see cref="ScoreHomeMeter"/>; the page's
    /// <c>MeasureBuilder.RevertMeterToHome</c>, which first asks <see cref="LeftHome"/>).</summary>
    public void RevertToHome()
    {
        Meter = HomeMeter;
        SenzaMisura = HomeSenzaMisura;
    }

    /// <summary>
    /// A section boundary, whole: the section's own header <c>time</c> if it states one
    /// (<see cref="SetTime(TimeSignatureSyntax)"/> — a <c>time none</c> header is no meter at all and leaves the running
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
    /// (the page's <c>MeasureBuilder.ResetPerMeasureState</c> at every measure close).</summary>
    public void SpendPartial() => Partial = null;
}
