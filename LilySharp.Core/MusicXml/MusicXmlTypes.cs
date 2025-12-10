using System.Xml.Linq;

namespace LilySharp.Core.MusicXml;

/// <summary>
/// Represents a MusicXML document.
/// </summary>
public sealed class MusicXmlDocument
{
    public string? Title { get; set; }
    public string? Composer { get; set; }
    public List<MusicXmlPart> Parts { get; } = new();
    
    /// <summary>
    /// Converts to XML document.
    /// </summary>
    public XDocument ToXml()
    {
        var scorePartwise = new XElement("score-partwise",
            new XAttribute("version", "4.0"));
        
        // Work info
        if (!string.IsNullOrEmpty(Title))
        {
            scorePartwise.Add(new XElement("work",
                new XElement("work-title", Title)));
        }
        
        // Identification
        if (!string.IsNullOrEmpty(Composer))
        {
            scorePartwise.Add(new XElement("identification",
                new XElement("creator", new XAttribute("type", "composer"), Composer)));
        }
        
        // Part list
        var partList = new XElement("part-list");
        for (int i = 0; i < Parts.Count; i++)
        {
            var part = Parts[i];
            partList.Add(new XElement("score-part",
                new XAttribute("id", $"P{i + 1}"),
                new XElement("part-name", part.Name ?? $"Part {i + 1}")));
        }
        scorePartwise.Add(partList);
        
        // Parts
        for (int i = 0; i < Parts.Count; i++)
        {
            scorePartwise.Add(Parts[i].ToXml($"P{i + 1}"));
        }
        
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("score-partwise", "-//Recordare//DTD MusicXML 4.0 Partwise//EN",
                "http://www.musicxml.org/dtds/partwise.dtd", null),
            scorePartwise);
    }
    
    /// <summary>
    /// Saves to file.
    /// </summary>
    public void Save(string path)
    {
        ToXml().Save(path);
    }
}

/// <summary>
/// Represents a part in MusicXML.
/// </summary>
public sealed class MusicXmlPart
{
    public string? Name { get; set; }
    public List<MusicXmlMeasure> Measures { get; } = new();
    
    public XElement ToXml(string id)
    {
        var part = new XElement("part", new XAttribute("id", id));
        foreach (var measure in Measures)
        {
            part.Add(measure.ToXml());
        }
        return part;
    }
}

/// <summary>
/// Represents a measure in MusicXML.
/// </summary>
public sealed class MusicXmlMeasure
{
    public int Number { get; set; }
    public MusicXmlAttributes? Attributes { get; set; }
    public MusicXmlDirection? Direction { get; set; }
    public List<MusicXmlNote> Notes { get; } = new();
    
    public XElement ToXml()
    {
        var measure = new XElement("measure", new XAttribute("number", Number));
        
        if (Attributes != null)
            measure.Add(Attributes.ToXml());
        
        if (Direction != null)
            measure.Add(Direction.ToXml());
        
        foreach (var note in Notes)
            measure.Add(note.ToXml());
        
        return measure;
    }
}

/// <summary>
/// Measure attributes (time signature, key, clef, divisions).
/// </summary>
public sealed class MusicXmlAttributes
{
    public int Divisions { get; set; } = 1;
    public int? KeyFifths { get; set; }
    public string? KeyMode { get; set; }
    public int? TimeBeats { get; set; }
    public int? TimeBeatType { get; set; }
    public string? ClefSign { get; set; }
    public int? ClefLine { get; set; }
    
    public XElement ToXml()
    {
        var attrs = new XElement("attributes",
            new XElement("divisions", Divisions));
        
        if (KeyFifths.HasValue)
        {
            attrs.Add(new XElement("key",
                new XElement("fifths", KeyFifths.Value),
                KeyMode != null ? new XElement("mode", KeyMode) : null));
        }
        
        if (TimeBeats.HasValue && TimeBeatType.HasValue)
        {
            attrs.Add(new XElement("time",
                new XElement("beats", TimeBeats.Value),
                new XElement("beat-type", TimeBeatType.Value)));
        }
        
        if (ClefSign != null)
        {
            attrs.Add(new XElement("clef",
                new XElement("sign", ClefSign),
                ClefLine.HasValue ? new XElement("line", ClefLine.Value) : null));
        }
        
        return attrs;
    }
}

/// <summary>
/// Direction (tempo, dynamics).
/// </summary>
public sealed class MusicXmlDirection
{
    public string? DynamicType { get; set; }
    public int? Tempo { get; set; }
    
    public XElement ToXml()
    {
        var direction = new XElement("direction", new XAttribute("placement", "above"));
        
        if (DynamicType != null)
        {
            direction.Add(new XElement("direction-type",
                new XElement("dynamics",
                    new XElement(DynamicType))));
        }
        
        if (Tempo.HasValue)
        {
            direction.Add(new XElement("direction-type",
                new XElement("metronome",
                    new XElement("beat-unit", "quarter"),
                    new XElement("per-minute", Tempo.Value))));
            direction.Add(new XElement("sound", new XAttribute("tempo", Tempo.Value)));
        }
        
        return direction;
    }
}

/// <summary>
/// Represents a note in MusicXML.
/// </summary>
public sealed class MusicXmlNote
{
    public bool IsRest { get; set; }
    public bool IsChord { get; set; }
    public string? Step { get; set; }
    public int? Alter { get; set; }
    public int? Octave { get; set; }
    public int Duration { get; set; }
    public string? Type { get; set; }
    public int Dots { get; set; }
    public string? Dynamic { get; set; }
    public List<string> Articulations { get; } = new();
    public bool IsGrace { get; set; }
    
    public XElement ToXml()
    {
        var note = new XElement("note");
        
        if (IsGrace)
            note.Add(new XElement("grace"));
        
        if (IsChord)
            note.Add(new XElement("chord"));
        
        if (IsRest)
        {
            note.Add(new XElement("rest"));
        }
        else
        {
            var pitch = new XElement("pitch",
                new XElement("step", Step),
                Alter.HasValue && Alter.Value != 0 ? new XElement("alter", Alter.Value) : null,
                new XElement("octave", Octave));
            note.Add(pitch);
        }
        
        if (!IsGrace)
            note.Add(new XElement("duration", Duration));
        
        if (Type != null)
            note.Add(new XElement("type", Type));
        
        for (int i = 0; i < Dots; i++)
            note.Add(new XElement("dot"));
        
        // Notations (articulations)
        if (Articulations.Count > 0)
        {
            var notations = new XElement("notations");
            var artics = new XElement("articulations");
            foreach (var a in Articulations)
                artics.Add(new XElement(a));
            notations.Add(artics);
            note.Add(notations);
        }
        
        return note;
    }
}