using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout information for an ottava bracket.
/// All coordinates are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/ottava-bracket.cc Ottava_bracket::print()
/// LILYPOND-REF: scm/define-grobs.scm:2445-2468 OttavaBracket grob defaults
/// </remarks>
public readonly record struct OttavaBracketLayout(
    /// <summary>Start measure index (for system Y lookup).</summary>
    int StartMeasureIndex,
    /// <summary>Start X position (staff spaces from score start).</summary>
    double StartX,
    /// <summary>End X position.</summary>
    double EndX,
    /// <summary>Y position (staff spaces from staff top).</summary>
    double Y,
    /// <summary>Display text (e.g., "8va", "8vb", "15ma", "15mb").</summary>
    string Text,
    /// <summary>Whether the bracket is above the staff (true) or below (false).</summary>
    bool IsAbove,
    /// <summary>Edge height for the end hook (in staff spaces).</summary>
    double EdgeHeight,
    /// <summary>Dash period for the dashed line.</summary>
    double DashPeriod,
    /// <summary>Dash fraction for the dashed line.</summary>
    double DashFraction,
    /// <summary>Source position for click-to-source mapping.</summary>
    int SourcePosition
);

/// <summary>
/// Calculates positions for ottava brackets.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/ottava-bracket.cc Ottava_bracket::print()
/// LILYPOND-REF: scm/define-grobs.scm:2445-2468 OttavaBracket grob defaults
///
/// OttavaBracket parameters from LilyPond:
/// - dash-fraction: 0.3
/// - edge-height: (0 . 0.8) — no hook at start, 0.8 staff spaces hook at end
/// - staff-padding: 2.0
/// - padding: 0.5
/// - shorten-pair: (-0.8 . -0.6)
/// - minimum-length: 0.3
/// - font-series: bold
/// - font-shape: italic
/// </remarks>
public static class OttavaBracketEngraver
{
    /// <summary>
    /// Dash fraction for the dashed line.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2449 (dash-fraction . 0.3)</remarks>
    private const double DashFraction = 0.3;

    /// <summary>
    /// Dash period (implicit from LilyPond's default line rendering).
    /// </summary>
    private const double DashPeriod = 2.0;

    /// <summary>
    /// Edge height at the end (right hook).
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2451 (edge-height . (0 . 0.8))</remarks>
    private const double EndEdgeHeight = 0.8;

    /// <summary>
    /// Staff padding — minimum distance from staff.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2462 (staff-padding . 2.0)</remarks>
    private const double StaffPadding = 2.0;

    /// <summary>
    /// Y position above staff for 8va/15ma brackets.
    /// </summary>
    private const double AboveStaffY = -StaffPadding;

    /// <summary>
    /// Y position below staff for 8vb/15mb brackets.
    /// StaffHeight (4) + padding.
    /// </summary>
    private const double BelowStaffY = 4.0 + StaffPadding;

    /// <summary>
    /// Left shorten (extends bracket slightly left).
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2460 (shorten-pair . (-0.8 . -0.6))</remarks>
    private const double LeftShorten = -0.8;

    /// <summary>
    /// Right shorten (extends bracket slightly right).
    /// </summary>
    private const double RightShorten = -0.6;

    /// <summary>
    /// Estimated text width per character for bold italic (staff spaces).
    /// </summary>
    private const double CharWidth = 0.65;

    /// <summary>
    /// Padding between text and line start.
    /// </summary>
    private const double TextLinePadding = 0.5;

    /// <summary>
    /// Calculates layout for all ottava brackets.
    /// </summary>
    public static ImmutableArray<OttavaBracketLayout> Calculate(
        ImmutableArray<OttavaBracketItem> ottavaBrackets,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts)
    {
        if (ottavaBrackets.IsDefaultOrEmpty)
            return ImmutableArray<OttavaBracketLayout>.Empty;

        // Build measure-to-system-index mapping
        // LILYPOND-REF: lily/ottava-bracket.cc — brackets split at system breaks
        var measureToSystemIdx = new Dictionary<int, int>();
        for (int si = 0; si < systems.Length; si++)
        {
            foreach (var m in systems[si].Measures)
                measureToSystemIdx[m.MeasureIndex] = si;
        }

        var layouts = ImmutableArray.CreateBuilder<OttavaBracketLayout>();

        foreach (var bracket in ottavaBrackets)
        {
            if (bracket.StartMeasureIndex >= measureLayouts.Length)
                continue;

            int endMeasureIdx = Math.Min(bracket.EndMeasureIndex, measureLayouts.Length - 1);

            // Determine vertical position and direction
            bool isAbove = bracket.Type == OttavaType.Ottava8va ||
                           bracket.Type == OttavaType.Quindicesima15ma;
            double y = isAbove ? AboveStaffY : BelowStaffY;

            // Display text
            string text = bracket.Type switch
            {
                OttavaType.Ottava8va => "8va",
                OttavaType.Ottava8vb => "8vb",
                OttavaType.Quindicesima15ma => "15ma",
                OttavaType.Quindicesima15mb => "15mb",
                _ => "8va"
            };

            // Determine which systems the bracket spans
            int startSystemIdx = measureToSystemIdx.GetValueOrDefault(bracket.StartMeasureIndex, 0);
            int endSystemIdx = measureToSystemIdx.GetValueOrDefault(endMeasureIdx, startSystemIdx);

            if (startSystemIdx == endSystemIdx)
            {
                // Same system — single bracket
                var startMeasure = measureLayouts[bracket.StartMeasureIndex];
                var endMeasure = measureLayouts[endMeasureIdx];
                layouts.Add(new OttavaBracketLayout(
                    StartMeasureIndex: bracket.StartMeasureIndex,
                    StartX: startMeasure.X + LeftShorten,
                    EndX: endMeasure.X + endMeasure.Width + RightShorten,
                    Y: y,
                    Text: text,
                    IsAbove: isAbove,
                    EdgeHeight: EndEdgeHeight,
                    DashPeriod: DashPeriod,
                    DashFraction: DashFraction,
                    SourcePosition: bracket.SourcePosition
                ));
            }
            else
            {
                // Cross-system: split into one bracket per system
                for (int si = startSystemIdx; si <= endSystemIdx; si++)
                {
                    var system = systems[si];
                    if (system.Measures.IsDefaultOrEmpty)
                        continue;

                    // Find this system's first and last measure indices
                    int sysFirstMeasure = system.Measures[0].MeasureIndex;
                    int sysLastMeasure = system.Measures[^1].MeasureIndex;

                    // Bracket start/end within this system
                    int segStart = si == startSystemIdx ? bracket.StartMeasureIndex : sysFirstMeasure;
                    int segEnd = si == endSystemIdx ? endMeasureIdx : sysLastMeasure;

                    if (segStart >= measureLayouts.Length || segEnd >= measureLayouts.Length)
                        continue;

                    var segStartMeasure = measureLayouts[segStart];
                    var segEndMeasure = measureLayouts[segEnd];

                    // First segment: show text + open end (no hook)
                    // Continuation segments: show "(8va)" text + open start
                    // Last segment: dashed line + hook
                    bool isFirst = (si == startSystemIdx);
                    bool isLast = (si == endSystemIdx);

                    double startX = isFirst
                        ? segStartMeasure.X + LeftShorten
                        : segStartMeasure.X + LeftShorten;
                    double endX = isLast
                        ? segEndMeasure.X + segEndMeasure.Width + RightShorten
                        : segEndMeasure.X + segEndMeasure.Width + RightShorten;

                    string segText = isFirst ? text : $"({text})";
                    double segEdgeHeight = isLast ? EndEdgeHeight : 0;

                    layouts.Add(new OttavaBracketLayout(
                        StartMeasureIndex: segStart,
                        StartX: startX,
                        EndX: endX,
                        Y: y,
                        Text: segText,
                        IsAbove: isAbove,
                        EdgeHeight: segEdgeHeight,
                        DashPeriod: DashPeriod,
                        DashFraction: DashFraction,
                        SourcePosition: bracket.SourcePosition
                    ));
                }
            }
        }

        return layouts.ToImmutable();
    }

    /// <summary>
    /// Detects ottava bracket spans from music marks.
    /// An ottava starts at an 8va/8vb/15ma/15mb mark and ends at
    /// loco or the next ottava mark.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/ottava-engraver.cc process_music() and stop_translation_timestep()
    /// </remarks>
    public static ImmutableArray<OttavaBracketItem> DetectOttavaBrackets(
        ImmutableArray<MusicMarkItem> musicMarks)
    {
        var brackets = ImmutableArray.CreateBuilder<OttavaBracketItem>();

        var ottavaMarks = musicMarks
            .Where(m => m.Type == MusicMarkType.OttavaUp ||
                        m.Type == MusicMarkType.OttavaDown ||
                        m.Type == MusicMarkType.QuindicesUp ||
                        m.Type == MusicMarkType.QuindicesDown ||
                        m.Type == MusicMarkType.Loco)
            .OrderBy(m => m.MeasureIndex)
            .ToList();

        if (ottavaMarks.Count == 0)
            return ImmutableArray<OttavaBracketItem>.Empty;

        // Walk through marks: each non-loco mark starts a bracket,
        // terminated by the next ottava/loco mark
        for (int i = 0; i < ottavaMarks.Count; i++)
        {
            var mark = ottavaMarks[i];

            // Skip loco marks (they only terminate, don't start)
            if (mark.Type == MusicMarkType.Loco)
                continue;

            OttavaType type = mark.Type switch
            {
                MusicMarkType.OttavaUp => OttavaType.Ottava8va,
                MusicMarkType.OttavaDown => OttavaType.Ottava8vb,
                MusicMarkType.QuindicesUp => OttavaType.Quindicesima15ma,
                MusicMarkType.QuindicesDown => OttavaType.Quindicesima15mb,
                _ => OttavaType.Ottava8va
            };

            // Find the end: next ottava/loco mark
            int endMeasure;
            if (i + 1 < ottavaMarks.Count)
            {
                endMeasure = ottavaMarks[i + 1].MeasureIndex;
            }
            else
            {
                // No end found — extend to one measure after the start
                endMeasure = mark.MeasureIndex + 1;
            }

            if (endMeasure > mark.MeasureIndex)
            {
                brackets.Add(new OttavaBracketItem(
                    Type: type,
                    StartMeasureIndex: mark.MeasureIndex,
                    EndMeasureIndex: endMeasure,
                    SourcePosition: mark.SourcePosition
                ));
            }
        }

        return brackets.ToImmutable();
    }
}
