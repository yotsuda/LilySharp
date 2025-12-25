using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// A dynamic marking attached to a music item.
/// </summary>
/// <remarks>
/// LILYPOND-REF: dynamic-engraver.cc:36-61 Dynamic_engraver class
/// LILYPOND-REF: define-grobs.scm:1298-1327 DynamicText grob definition
/// </remarks>
public sealed record DynamicItem
{
    /// <summary>The dynamic level (ppp to fff).</summary>
    public DynamicLevel Level { get; }
    
    /// <summary>The text representation of this dynamic.</summary>
    public string Text { get; }
    
    /// <summary>The measure index where this dynamic appears.</summary>
    public int MeasureIndex { get; }
    
    /// <summary>The item index within the measure.</summary>
    public int ItemIndex { get; }
    
    /// <summary>Source position for click-to-source mapping.</summary>
    public int SourcePosition { get; }
    
    public DynamicItem(DynamicLevel level, int measureIndex, int itemIndex, int sourcePosition)
    {
        Level = level;
        Text = GetDynamicText(level);
        MeasureIndex = measureIndex;
        ItemIndex = itemIndex;
        SourcePosition = sourcePosition;
    }
    
    private static string GetDynamicText(DynamicLevel level) => level switch
    {
        DynamicLevel.PPP => "ppp",
        DynamicLevel.PP => "pp",
        DynamicLevel.P => "p",
        DynamicLevel.MP => "mp",
        DynamicLevel.MF => "mf",
        DynamicLevel.F => "f",
        DynamicLevel.FF => "ff",
        DynamicLevel.FFF => "fff",
        _ => ""
    };
}
