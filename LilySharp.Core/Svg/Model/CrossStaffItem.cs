namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Marks a note/chord that should be rendered on a different staff
/// than the voice it belongs to (cross-staff notation).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/beam.cc:1451-1459 - Beam::is_cross_staff
/// LILYPOND-REF: lily/stem.cc:1168-1179 - Stem::is_cross_staff
///
/// In LilyPond, cross-staff is achieved with \change Staff = "name".
/// In LilySharp, the @cross annotation on a note marks it for rendering
/// on the other staff in a grand staff context.
///
/// Common use case: piano music where notes from one hand temporarily
/// appear on the other staff, connected by a beam.
/// </remarks>
public sealed record CrossStaffItem(
    /// <summary>Measure containing the cross-staff note.</summary>
    int MeasureIndex,

    /// <summary>Item index within the measure.</summary>
    int ItemIndex,

    /// <summary>
    /// Target staff index within the staff group (0-based).
    /// For grand staff: 0 = treble, 1 = bass.
    /// -1 means revert to original staff.
    /// </summary>
    int TargetStaffIndex,

    /// <summary>Source position for diagnostics.</summary>
    int SourcePosition
);
