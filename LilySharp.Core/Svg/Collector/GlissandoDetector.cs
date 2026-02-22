using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Detects glissandos between notes marked with HasGlissando.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/glissando-engraver.cc - Glissando_engraver
/// Unlike ties (same pitch), glissandos connect to the next note of any pitch.
/// </remarks>
public sealed class GlissandoDetector
{
    public ImmutableArray<GlissandoItem> DetectGlissandos(Score score)
    {
        var glissandos = new List<GlissandoItem>();
        var measures = score.Voice.Measures;

        for (int measureIdx = 0; measureIdx < measures.Length; measureIdx++)
        {
            var measure = measures[measureIdx];

            for (int itemIdx = 0; itemIdx < measure.Items.Length; itemIdx++)
            {
                if (measure.Items[itemIdx] is not NoteItem startNote)
                    continue;

                if (!startNote.HasGlissando)
                    continue;

                var endNote = FindNextNote(measures, measureIdx, itemIdx);
                if (endNote != null)
                {
                    var (endMeasureIdx, endItemIdx, note) = endNote.Value;

                    glissandos.Add(new GlissandoItem(
                        StartMeasureIndex: measureIdx,
                        StartItemIndex: itemIdx,
                        StartStaffPosition: startNote.StaffPosition,
                        EndMeasureIndex: endMeasureIdx,
                        EndItemIndex: endItemIdx,
                        EndStaffPosition: note.StaffPosition,
                        Style: GlissandoStyle.Line,
                        SourcePosition: startNote.SourcePosition));
                }
            }
        }

        return glissandos.ToImmutableArray();
    }

    /// <summary>
    /// Finds the next note after the given position (any pitch).
    /// </summary>
    private static (int measureIdx, int itemIdx, NoteItem note)? FindNextNote(
        ImmutableArray<Measure> measures,
        int startMeasureIdx,
        int startItemIdx)
    {
        // Search in current measure first
        var currentMeasure = measures[startMeasureIdx];
        for (int i = startItemIdx + 1; i < currentMeasure.Items.Length; i++)
        {
            if (currentMeasure.Items[i] is NoteItem candidate)
                return (startMeasureIdx, i, candidate);
        }

        // Search in subsequent measures
        for (int m = startMeasureIdx + 1; m < measures.Length; m++)
        {
            var measure = measures[m];
            for (int i = 0; i < measure.Items.Length; i++)
            {
                if (measure.Items[i] is NoteItem candidate)
                    return (m, i, candidate);
            }
        }

        return null;
    }
}
