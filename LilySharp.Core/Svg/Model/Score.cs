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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Time signature (e.g., 3/4, 4/4, 6/8).
/// </summary>
public readonly record struct TimeSignature(int Beats, int BeatType, string? BeatsText = null, bool SenzaMisura = false)
{
    /// <summary>The numerator AS PRINTED — the additive spelling when there is one
    /// ("3+2"), otherwise the beat count.</summary>
    /// <remarks>
    /// ⚠️ THE RESERVATION READS THIS, not <see cref="Beats"/>, because the drawing does
    /// (<c>SharedRenderer.DrawTimeSignature</c>) and the two have to be one run
    /// (<see cref="Layout.MeterGlyphRun"/>). It replaced a <c>LayoutBeats</c> that
    /// approximated an additive numerator by "a number with as many digits"
    /// (<c>10^(len-1)</c>, so "3+2" booked the width of "100") — which was a proxy for a
    /// width nobody could measure while the reservation could only take ONE digit anyway.
    /// Now the row is laid out, the proxy has nothing left to do, and the <c>+</c> is the
    /// only piece whose width is still Lily#'s own (LilyPond spells a compound meter's
    /// numerator with its own markup; no ledger point watches one).
    /// </remarks>
    public string NumeratorText => BeatsText
        ?? Beats.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The denominator as printed — always a plain number.</summary>
    public string DenominatorText =>
        BeatType.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Duration of one full measure.</summary>
    public Fraction MeasureDuration => new(Beats, BeatType);

    /// <summary>Returns the printed form of the meter, e.g. <c>3/4</c>.</summary>
    public override string ToString() => $"{BeatsText ?? Beats.ToString()}/{BeatType}";
}

/// <summary>
/// Key signature (number of sharps or flats).
/// </summary>
public readonly record struct KeySignature(int Sharps, string? Custom = null)
{
    /// <summary>Positive for sharps, negative for flats.</summary>
    public bool IsSharps => Sharps > 0;
    /// <summary>True when the signature is expressed with flats.</summary>
    public bool IsFlats => Sharps < 0;

    /// <summary>Number of accidentals in the signature.</summary>
    public int Count => Custom != null ? DecodeCustom(Custom).Count() : Math.Abs(Sharps);

    /// <summary>Encodes custom (step, alter) pairs as "s:a;s:a" — a string
    /// keeps the record struct's VALUE equality (key-change detection).</summary>
    public static string EncodeCustom(IEnumerable<(int Step, int Alter)> pairs)
        => string.Join(";", pairs.Select(p => $"{p.Step}:{p.Alter}"));

    /// <summary>Decodes the custom pairs in print order.</summary>
    public static IEnumerable<(int Step, int Alter)> DecodeCustom(string custom)
    {
        foreach (var part in custom.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = part.Split(':');
            if (bits.Length == 2 && int.TryParse(bits[0], out int s) && int.TryParse(bits[1], out int a))
                yield return (s, a);
        }
    }

    /// <summary>The C major / A minor key signature (no accidentals).</summary>
    public static readonly KeySignature CMajor = new(0);
}

/// <summary>
/// A voice (part) containing a sequence of measures.
/// </summary>
public sealed record Voice(
    string Name,
    ImmutableArray<Measure> Measures
);

/// <summary>
/// A complete musical score ready for layout.
/// </summary>
/// <remarks>
/// Score is the output of the collection phase and input to the layout phase.
/// It contains all musical content in a structured, measure-based format.
/// </remarks>
public sealed record Score
{
    /// <summary>All voices in the score.</summary>
    public ImmutableArray<Voice> Voices { get; }

    /// <summary>The primary voice (first voice, for backward compatibility).</summary>
    public Voice Voice => Voices[0];

    /// <summary>Time signature for the score.</summary>
    public TimeSignature TimeSignature { get; }

    /// <summary>Key signature for the score.</summary>
    public KeySignature KeySignature { get; }

    /// <summary>Clef type ("treble", "bass", "alto", "tenor").</summary>
    public string Clef { get; }

    /// <summary>Tempo in BPM (optional).</summary>
    public int? Tempo { get; }

    /// <summary>Textual tempo marking ("Grave") for the opening metronome mark.</summary>
    public string? TempoText { get; init; }

    /// <summary>Metronome beat unit of the opening tempo (4 = quarter).</summary>
    public int TempoBeatUnit { get; init; } = 4;

    /// <summary>Augmentation dots on the opening tempo's beat unit.</summary>
    public int TempoDots { get; init; }

    /// <summary>The note value the initial tempo asked to swing (0 = none, 8 = eighths,
    /// 16 = sixteenths), drawn as the swing-feel equation beside the metronome mark.</summary>
    public int SwingSubdivision { get; }

    /// <summary>Title (optional).</summary>
    public string? Title { get; }

    /// <summary>Composer (optional).</summary>
    public string? Composer { get; }

    /// <summary>
    /// Which face each kind of non-music text is drawn in, from the <c>font</c> header
    /// directive. Never null — a score without one carries
    /// <see cref="Rendering.TextFontPlan.Default"/>, so no reader has to spell the
    /// "nothing was asked for" case a second way.
    /// </summary>
    public Rendering.TextFontPlan Fonts { get; init; } = Rendering.TextFontPlan.Default;

    /// <summary>
    /// The page's dimensions, from the <c>paper { }</c> header directive. Never null,
    /// the same convention as <see cref="Fonts"/> — a score without one carries
    /// <c>LayoutOptions.Default</c>, so handing this to <c>LayoutEngine</c> is always
    /// right and a book with no directive lays out exactly as before.
    /// </summary>
    internal Layout.LayoutOptions Paper { get; init; } = Layout.LayoutOptions.Default;

    /// <summary>
    /// The text measurements this score's <see cref="Fonts"/> imply — see
    /// <see cref="MultiStaffScore.TextMetrics"/>, which is the same property on the shape
    /// the layout usually holds. Both carry it because both are handed to engravers.
    /// </summary>
    public Rendering.ScoreTextMetrics TextMetrics =>
        _textMetrics ??= Fonts.IsDefault
            ? Rendering.ScoreTextMetrics.Bundled
            : new Rendering.ScoreTextMetrics(Fonts);

    private Rendering.ScoreTextMetrics? _textMetrics;

    /// <summary>Dynamic markings in the score.</summary>
    public ImmutableArray<DynamicItem> Dynamics { get; }

    /// <summary>Articulation marks in the score.</summary>
    public ImmutableArray<ArticulationItem> Articulations { get; }

    /// <summary>Grace notes in the score.</summary>
    public ImmutableArray<GraceNoteItem> GraceNotes { get; }


    /// <summary>Lyrics in the score.</summary>
    public ImmutableArray<LyricItem> Lyrics { get; }

    /// <summary>Music marks (segno, coda, fine, D.S., D.C., etc.) in the score.</summary>
    public ImmutableArray<MusicMarkItem> MusicMarks { get; }

    /// <summary>Custom text annotations in the score.</summary>
    public ImmutableArray<CustomTextItem> CustomTexts { get; }

    /// <summary>Volta brackets (first/second ending) in the score.</summary>
    public ImmutableArray<VoltaBracketItem> VoltaBrackets { get; }

    /// <summary>Tuplet brackets in the score.</summary>
    public ImmutableArray<TupletBracketItem> TupletBrackets { get; }

    /// <summary>Arpeggio markings in the score.</summary>
    public ImmutableArray<ArpeggioItem> Arpeggios { get; }

    /// <summary>Figured bass annotations in the score.</summary>
    public ImmutableArray<FiguredBassItem> FiguredBasses { get; }

    /// <summary>Chord name symbols in the score.</summary>
    public ImmutableArray<ChordNameItem> ChordNames { get; }

    /// <summary>Percent repeat markers in the score.</summary>
    public ImmutableArray<PercentRepeatItem> PercentRepeats { get; }

    /// <summary>Cross-staff note assignments for grand staff rendering.</summary>
    public ImmutableArray<CrossStaffItem> CrossStaffItems { get; }

    /// <summary>Grob property overrides from override declarations.</summary>
    public ImmutableArray<GrobOverride> GrobOverrides { get; }

    /// <summary>Grob property reverts from revert declarations.</summary>
    public ImmutableArray<GrobRevert> GrobReverts { get; }

    /// <summary>Trill spanners (tr + wavy line) in the score.</summary>
    public ImmutableArray<TrillSpannerItem> TrillSpanners { get; }

    /// <summary>Source offsets of the header grobs (title/composer/time/key) so the
    /// SVG can tag them with data-pos and the preview can click-to-jump.</summary>
    public HeaderPositions Header { get; }

    /// <summary>Whether this score has multiple voices.</summary>
    public bool IsMultiVoice => Voices.Length > 1;

    /// <summary>
    /// Creates a single-voice score (backward compatible constructor).
    /// </summary>
    public Score(
        Voice voice,
        TimeSignature timeSignature,
        KeySignature keySignature,
        string clef,
        int? tempo = null,
        string? title = null,
        string? composer = null,
        ImmutableArray<DynamicItem>? dynamics = null,
        ImmutableArray<ArticulationItem>? articulations = null,
        ImmutableArray<GraceNoteItem>? graceNotes = null,
        ImmutableArray<LyricItem>? lyrics = null,
        ImmutableArray<MusicMarkItem>? musicMarks = null,
        ImmutableArray<CustomTextItem>? customTexts = null,
        ImmutableArray<VoltaBracketItem>? voltaBrackets = null,
        ImmutableArray<TupletBracketItem>? tupletBrackets = null,
        ImmutableArray<ArpeggioItem>? arpeggios = null,
        ImmutableArray<FiguredBassItem>? figuredBasses = null,
        ImmutableArray<ChordNameItem>? chordNames = null,
        ImmutableArray<PercentRepeatItem>? percentRepeats = null,
        ImmutableArray<CrossStaffItem>? crossStaffItems = null,
        ImmutableArray<GrobOverride>? grobOverrides = null,
        ImmutableArray<GrobRevert>? grobReverts = null,
        ImmutableArray<TrillSpannerItem>? trillSpanners = null,
        HeaderPositions header = default,
        int swingSubdivision = 0)
        : this(ImmutableArray.Create(voice), timeSignature, keySignature, clef, tempo, title, composer, dynamics, articulations, graceNotes, lyrics, musicMarks, customTexts, voltaBrackets, tupletBrackets, arpeggios, figuredBasses, chordNames, percentRepeats, crossStaffItems, grobOverrides, grobReverts, trillSpanners, header, swingSubdivision)
    {
    }

    /// <summary>
    /// Creates a multi-voice score.
    /// </summary>
    public Score(
        ImmutableArray<Voice> voices,
        TimeSignature timeSignature,
        KeySignature keySignature,
        string clef,
        int? tempo = null,
        string? title = null,
        string? composer = null,
        ImmutableArray<DynamicItem>? dynamics = null,
        ImmutableArray<ArticulationItem>? articulations = null,
        ImmutableArray<GraceNoteItem>? graceNotes = null,
        ImmutableArray<LyricItem>? lyrics = null,
        ImmutableArray<MusicMarkItem>? musicMarks = null,
        ImmutableArray<CustomTextItem>? customTexts = null,
        ImmutableArray<VoltaBracketItem>? voltaBrackets = null,
        ImmutableArray<TupletBracketItem>? tupletBrackets = null,
        ImmutableArray<ArpeggioItem>? arpeggios = null,
        ImmutableArray<FiguredBassItem>? figuredBasses = null,
        ImmutableArray<ChordNameItem>? chordNames = null,
        ImmutableArray<PercentRepeatItem>? percentRepeats = null,
        ImmutableArray<CrossStaffItem>? crossStaffItems = null,
        ImmutableArray<GrobOverride>? grobOverrides = null,
        ImmutableArray<GrobRevert>? grobReverts = null,
        ImmutableArray<TrillSpannerItem>? trillSpanners = null,
        HeaderPositions header = default,
        int swingSubdivision = 0)
    {
        if (voices.Length == 0)
            throw new ArgumentException("Score must have at least one voice", nameof(voices));

        Voices = voices;
        TimeSignature = timeSignature;
        KeySignature = keySignature;
        Clef = clef;
        Tempo = tempo;
        SwingSubdivision = swingSubdivision;
        Title = title;
        Composer = composer;
        Dynamics = dynamics ?? ImmutableArray<DynamicItem>.Empty;
        Articulations = articulations ?? ImmutableArray<ArticulationItem>.Empty;
        GraceNotes = graceNotes ?? ImmutableArray<GraceNoteItem>.Empty;
        Lyrics = lyrics ?? ImmutableArray<LyricItem>.Empty;
        MusicMarks = musicMarks ?? ImmutableArray<MusicMarkItem>.Empty;
        CustomTexts = customTexts ?? ImmutableArray<CustomTextItem>.Empty;
        VoltaBrackets = voltaBrackets ?? ImmutableArray<VoltaBracketItem>.Empty;
        TupletBrackets = tupletBrackets ?? ImmutableArray<TupletBracketItem>.Empty;
        Arpeggios = arpeggios ?? ImmutableArray<ArpeggioItem>.Empty;
        FiguredBasses = figuredBasses ?? ImmutableArray<FiguredBassItem>.Empty;
        ChordNames = chordNames ?? ImmutableArray<ChordNameItem>.Empty;
        PercentRepeats = percentRepeats ?? ImmutableArray<PercentRepeatItem>.Empty;
        CrossStaffItems = crossStaffItems ?? ImmutableArray<CrossStaffItem>.Empty;
        GrobOverrides = grobOverrides ?? ImmutableArray<GrobOverride>.Empty;
        GrobReverts = grobReverts ?? ImmutableArray<GrobRevert>.Empty;
        TrillSpanners = trillSpanners ?? ImmutableArray<TrillSpannerItem>.Empty;
        Header = header;
    }

    /// <summary>Total number of measures in the score (from primary voice).</summary>
    public int MeasureCount => Voice.Measures.Length;
}
