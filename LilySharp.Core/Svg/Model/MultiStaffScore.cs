using System.Collections.Immutable;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// A complete musical score with multiple staff groups.
/// </summary>
/// <remarks>
/// MultiStaffScore extends the basic Score concept to support:
/// - Multiple staff groups (single staves, grand staves, bracketed groups)
/// - Different clefs per staff
/// - Voice-to-staff mapping
///
/// This is the primary model for rendering piano, organ, and orchestral scores.
/// </remarks>
public sealed record MultiStaffScore
{
    /// <summary>Staff groups in this score.</summary>
    public ImmutableArray<StaffGroup> StaffGroups { get; }

    /// <summary>Time signature for the score.</summary>
    public TimeSignature TimeSignature { get; }

    /// <summary>Key signature for the score.</summary>
    public KeySignature KeySignature { get; }

    /// <summary>Tempo in BPM (optional).</summary>
    public int? Tempo { get; }

    /// <summary>Title (optional).</summary>
    public string? Title { get; }

    /// <summary>Composer (optional).</summary>
    public string? Composer { get; }

    /// <summary>Lyrics in the score.</summary>
    public ImmutableArray<LyricItem> Lyrics { get; }

    /// <summary>Music marks (segno, coda, fine, D.S., etc.).</summary>
    public ImmutableArray<MusicMarkItem> MusicMarks { get; }

    /// <summary>Custom text annotations.</summary>
    public ImmutableArray<CustomTextItem> CustomTexts { get; }

    /// <summary>Whether this score has a grand staff.</summary>
    public bool HasGrandStaff => StaffGroups.Any(g => g.IsGrandStaff);

    /// <summary>Whether this score has multiple staff groups.</summary>
    public bool IsMultiStaff => StaffGroups.Length > 1 || HasGrandStaff;

    /// <summary>Total number of individual staves.</summary>
    public int TotalStaffCount => StaffGroups.Sum(g => g.StaffCount);

    public MultiStaffScore(
        ImmutableArray<StaffGroup> staffGroups,
        TimeSignature timeSignature,
        KeySignature keySignature,
        int? tempo = null,
        string? title = null,
        string? composer = null,
        ImmutableArray<LyricItem>? lyrics = null,
        ImmutableArray<MusicMarkItem>? musicMarks = null,
        ImmutableArray<CustomTextItem>? customTexts = null)
    {
        if (staffGroups.Length == 0)
            throw new ArgumentException("Score must have at least one staff group", nameof(staffGroups));

        StaffGroups = staffGroups;
        TimeSignature = timeSignature;
        KeySignature = keySignature;
        Tempo = tempo;
        Title = title;
        Composer = composer;
        Lyrics = lyrics ?? ImmutableArray<LyricItem>.Empty;
        MusicMarks = musicMarks ?? ImmutableArray<MusicMarkItem>.Empty;
        CustomTexts = customTexts ?? ImmutableArray<CustomTextItem>.Empty;
    }

    /// <summary>
    /// Creates a MultiStaffScore from a single-voice Score (for backward compatibility).
    /// </summary>
    public static MultiStaffScore FromScore(Score score)
    {
        var clef = Staff.ParseClef(score.Clef);
        var staff = Staff.Create(clef, score.Voice);
        var staffGroup = StaffGroup.CreateSingle(staff);

        return new MultiStaffScore(
            ImmutableArray.Create(staffGroup),
            score.TimeSignature,
            score.KeySignature,
            score.Tempo,
            score.Title,
            score.Composer,
            score.Lyrics,
            score.MusicMarks,
            score.CustomTexts);
    }

    /// <summary>Number of measures (from first staff of first group).</summary>
    public int MeasureCount => StaffGroups[0].PrimaryStaff.MeasureCount;

    /// <summary>Gets all voices across all staves.</summary>
    public IEnumerable<Voice> AllVoices
    {
        get
        {
            foreach (var group in StaffGroups)
            foreach (var staff in group.Staves)
            foreach (var voice in staff.Voices)
                yield return voice;
        }
    }

    /// <summary>
    /// Iterates over all staves with their group context.
    /// </summary>
    public IEnumerable<(StaffGroup Group, Staff Staff, int StaffIndexInGroup)> EnumerateStaves()
    {
        foreach (var group in StaffGroups)
        {
            for (int i = 0; i < group.Staves.Length; i++)
            {
                yield return (group, group.Staves[i], i);
            }
        }
    }
}