using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Detects slurs between notes in a score.
/// </summary>
public sealed class SlurDetector
{
    public ImmutableArray<SlurItem> DetectSlurs(Score score)
    {
        var slurs = new List<SlurItem>();
        var measures = score.Voice.Measures;
        var openSlurs = new Stack<(int measureIdx, int itemIdx, NoteItem note)>();

        for (int measureIdx = 0; measureIdx < measures.Length; measureIdx++)
        {
            var measure = measures[measureIdx];

            for (int itemIdx = 0; itemIdx < measure.Items.Length; itemIdx++)
            {
                if (measure.Items[itemIdx] is not NoteItem note)
                    continue;

                if (note.HasSlurStart)
                {
                    openSlurs.Push((measureIdx, itemIdx, note));
                }

                if (note.HasSlurEnd && openSlurs.Count > 0)
                {
                    var (startMeasureIdx, startItemIdx, startNote) = openSlurs.Pop();

                    // Slur curves opposite to stem direction
                    // NoteItem.StemUp: true = stem visually UP, false = stem visually DOWN
                    bool curveUp = !startNote.StemUp;

                    slurs.Add(new SlurItem(
                        startNote,
                        note,
                        startNote.StaffPosition,
                        note.StaffPosition,
                        curveUp,
                        startMeasureIdx,
                        measureIdx,
                        startItemIdx,
                        itemIdx));
                }
            }
        }

        return slurs.ToImmutableArray();
    }
}