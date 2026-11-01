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

using System.Collections.Generic;

namespace LilySharp.Core.Editing;

/// <summary>How an AI speaks of a finding (docs/review-tool-design.md §2).</summary>
public enum ReviewKind
{
    /// <summary>Almost surely unintended.</summary>
    Mistake,
    /// <summary>The music is right, and written hard to read.</summary>
    Notation,
    /// <summary>A choice to weigh.</summary>
    Style,
    /// <summary>A suggestion beyond a fault.</summary>
    Idea,
}

/// <summary>How sure a check's count is. A weak count lowers the tone: a low-confidence mistake is put as a question.</summary>
public enum ReviewConfidence
{
    /// <summary>The count reads the notation itself.</summary>
    High,
    /// <summary>The count reads a habit that usually means this.</summary>
    Medium,
    /// <summary>The count is a guess at the music's intent.</summary>
    Low,
}

/// <summary>What is said of one check, whichever piece it was made in.</summary>
/// <param name="Kind">How to speak of it.</param>
/// <param name="Confidence">How sure the count is.</param>
/// <param name="Why">The convention behind it, in a sentence or two: the teaching.</param>
/// <param name="FineWhen">The cases where it does not apply; null when it always does.</param>
public sealed record ReviewNote(ReviewKind Kind, ReviewConfidence Confidence, string Why, string? FineWhen = null);

/// <summary>
/// One note per check of <see cref="CompositionChecks"/>, so that every report says it alike
/// (docs/review-tool-design.md §2: the AI expands <c>why</c> for a beginner and shortens it for an
/// advanced reader, and checks the piece against <c>fine_when</c> before raising the point).
/// </summary>
public static class ReviewNotes
{
    private const ReviewKind M = ReviewKind.Mistake, N = ReviewKind.Notation, S = ReviewKind.Style, I = ReviewKind.Idea;
    private const ReviewConfidence H = ReviewConfidence.High, D = ReviewConfidence.Medium, L = ReviewConfidence.Low;

    /// <summary>Every check's note, by <see cref="CompositionFinding.Id"/>.</summary>
    public static IReadOnlyDictionary<string, ReviewNote> All { get; } = new Dictionary<string, ReviewNote>(System.StringComparer.Ordinal)
    {
        // The score and its parts
        ["score.title"] = new(N, H, "A title tells a player and a listener what the piece is, and finds it again in a list of files."),
        ["score.ensemble-braced"] = new(M, H, "A brace joins the staves one player reads, as a pianist's two hands. An ensemble's players each read their own staff, joined by a bracket.",
            "The staves really are one player's, as an organ's three."),
        ["part.no-instrument"] = new(M, H, "A part with no instrument plays as a piano, and is given a piano's range and clef.",
            "A piano is meant."),
        ["staff.hand-label"] = new(N, H, "A score's labels name instruments. A pianist knows which staff is which hand, and a part in an ensemble is named once.",
            "It is teaching material for a beginner, where the labels are the point."),
        ["staff.restates-chords"] = new(S, D, "A guitarist reads the chord row and its diagrams. Block chords under it say the same thing twice and fill the page.",
            "The staff shows a voicing or a rhythm the player must play exactly."),

        // Dynamics
        ["dynamics.part-silent"] = new(M, D, "Each player reads only their own staff, so a level written in another part does not reach them.",
            "The part stays at one level throughout, or follows a conductor in a sketch."),
        ["dynamics.drums-silent"] = new(S, D, "Drums set a band's loudness more than any other part, and a quiet passage over full drums is not quiet."),
        ["dynamics.rhythm-section-flat"] = new(S, D, "A band rises and falls together. A bass and drums at one level flatten the sections the lead tries to shape.",
            "The steady level is the style, as in some dance music or a motorik groove."),
        ["dynamics.drop"] = new(S, D, "A sudden fall of two levels or more is heard as a cut. It is written as one (subito) when meant, and prepared with a diminuendo when not.",
            "The drop is meant as a surprise."),
        ["dynamics.set-not-shaped"] = new(S, D, "Hairpins are how a level gets from one place to the next. Levels that only switch at section lines sound like a switch.",
            "Terraced dynamics are the style, as in much baroque music."),
        ["dynamics.late"] = new(N, D, "A player needs a level at their first note, or the first section is played at a guess."),

        // The band
        ["band.no-drums"] = new(I, L, "A band's groove is carried by the drums, locked with the bass.", "An acoustic or drumless line-up is meant."),
        ["band.no-bass"] = new(I, L, "A bass joins the drums' rhythm to the chords' harmony, and a band without one sounds thin underneath.", "The line-up is meant to have no bass, or a keyboard's left hand plays it."),
        ["band.no-chord-part"] = new(I, L, "Something in a band has to state the harmony: a guitar or keyboards.", "A bass and a lead are all that is meant, as in a duo."),
        ["band.tempo-changes"] = new(S, D, "A band usually holds one tempo, because the groove depends on it.", "A tempo change is the arrangement's own idea, as a half-time ending or a ritardando."),
        ["band.nobody-plays-chords"] = new(S, D, "With only single notes over the bass, nothing states the harmony and the sound is thin.", "A sparse, riff-based texture is meant, as in some funk or math rock."),
        ["band.no-tune"] = new(S, D, "An instrumental's tune is what a listener takes away. Riffs and chords are the ground it stands on.", "The piece is a groove or backing track."),
        ["band.register-with-bass"] = new(S, D, "Two low lines in one register blur each other, since low intervals are hard to hear apart.", "The part doubles the bass on purpose, as a unison riff."),
        ["band.one-player-two-voices"] = new(M, D, "One player's two hands cannot strike chords and play a separate tune at once on a fretted instrument, except in a few fingerstyle shapes.", "It is two players written on one part."),
        ["bass.leaps"] = new(S, D, "A band's bass drives the root with the kick drum. Leaps of more than an octave from note to note break the line.", "The style leaps by nature, as slap bass."),
        ["drums.one-groove"] = new(S, L, "Sections are told apart partly by what the drums do: hi-hat, ride, half-time.", "A single hypnotic groove is the point of the piece."),
        ["drums.one-beat"] = new(S, L, "Changing the cymbals changes the colour, and changing the kick and snare changes the feel.", "A single hypnotic groove is the point of the piece."),
        ["drums.same-bar"] = new(S, D, "A drummer marks the phrases: a fill leads into the next one, and a crash lands on it.", "A loop or a machine feel is meant."),
        ["drums.crashes-off-phrases"] = new(M, D, "A crash marks where a phrase begins. Off those bars it is heard as a misplaced accent.", "An off-phrase accent is the arrangement's own idea."),

        // The singers
        ["singers.octaves"] = new(S, D, "Two parts in octaves are heard as one line, and the harmony loses a voice.", "A unison or octave passage is meant, as a climax."),
        ["singers.crossed"] = new(M, D, "When a lower voice stands above the part over it, neither is heard as its own line.", "A brief crossing is written for a reason, as in counterpoint."),
        ["singers.crossed-at-end"] = new(M, H, "The last chord is where the voices are heard most plainly, and a crossing there usually means an octave mark has slipped."),
        ["singers.inner-leaps"] = new(S, D, "A singer holds a line by ear, and leap after leap between chord tones has no line to hold.", "The part is written for trained singers in a style that leaps."),
        ["singers.no-words"] = new(M, D, "A voice sings words, and bars with none usually mean a lyric track has not reached them.", "A vocalise, a hum or an ah is meant."),
        ["singers.no-breath"] = new(M, D, "A singer has to breathe, and a phrase of words ends on a longer note with a rest after it.", "Staggered breathing in a choir, or a wordless drone."),
        ["piano.holds"] = new(S, D, "An accompaniment moves a song on. Held chords under every bar leave it standing still.", "A still, chorale-like setting is meant."),

        // The accompaniments and the harmony
        ["part.one-figure"] = new(S, L, "A contrasting section usually takes a figure of its own, which is how the ear knows it has arrived.", "One figure throughout is the design: minimalism, an ostinato, a passacaglia."),
        ["chord.outside"] = new(S, L, "A chord outside the key usually has a job: it leads to the chord after it, or is borrowed from the parallel key. One with no job can be colour, and can be a slip.", "The style moves freely between keys, as jazz, film music and much rock do."),
        ["harmony.one-progression"] = new(S, L, "Sections are told apart by their harmony as much as by their tune.", "A blues, a vamp or a loop-based style, where one progression is the form."),
        ["harmony.clash"] = new(M, D, "A melody note held on the beat a semitone from the harmony is heard as a wrong note, not as colour.", "It is a blue note, or a suspension that resolves."),

        // The melody
        ["rhythm.one-value"] = new(S, D, "A line lives on its rhythm: long notes at its peaks, shorter ones leading to them, and rests to breathe.", "An even pace is the music's character, as in a perpetuum mobile, a chorale or a bass line."),
        ["rhythm.same-bars"] = new(S, D, "One bar's rhythm on most bars is a pattern poured full. A phrase's rhythm follows its words and its shape.", "A strict dance rhythm or an ostinato is the design."),
        ["rhythm.same-bars-in-section"] = new(S, D, "One rhythm through a whole section gives the ear nothing to follow inside it.", "A strict dance rhythm or an ostinato is the design."),
        ["melody.leaps"] = new(S, D, "A tune moves mostly by step, with a leap now and then to reach a peak, turning back by step after it.", "The theme is a fanfare, a call on its chord's notes."),
        ["melody.sequence"] = new(S, D, "One bar's figure moved along the chords is a sequence. Used throughout, it stops being a phrase with somewhere to go.", "A sequence is the music's own design, as in a baroque episode."),
        ["melody.peak-early"] = new(S, D, "A song usually climbs. Its highest note, spent in the first phrase, leaves the chorus nowhere to go.", "The song is built to fall from its opening, as some ballads are."),
        ["meter.across-beats"] = new(M, D, "In a compound meter a beat is three eighths, and a note crossing the beats is heard as another meter.", "A syncopation or a hemiola is meant."),
        ["tune.under-chords"] = new(S, D, "The ear takes the top note for the tune, so a chord reaching over it buries the one written.", "An inner melody is meant."),
        ["phrase.formula"] = new(S, D, "One rhythm in every phrase, verse and chorus alike, is a formula and not a shape. A hook is remembered for a rhythm of its own.", "A strophic song or a hymn repeats its rhythm by design."),
        ["phrase.positions"] = new(S, D, "Every section singing the same four bars of rhythm leaves the sections sounding alike.", "A strophic song or a hymn repeats its rhythm by design."),
        ["phrase.endings"] = new(S, L, "One closing gesture for every line becomes predictable: some lines can end on a held note, others run on.", "A refrain's closing rhythm is the song's signature."),
        ["phrase.closing-figure"] = new(S, D, "Sections closing on one figure sound as though the song has run out of ways to end.", "The figure is the song's refrain."),
        ["phrase.note-counts"] = new(S, L, "The same number of notes in each bar's place suggests words cut to one mould.", "The verse form or the language sets the count."),
        ["phrase.open-endings"] = new(S, L, "A section usually closes its phrase on the chord's own tone, and an open interval over the bass asks for more.", "The section is meant to lead straight into the next."),
        ["chorus.opens-like-another"] = new(S, D, "A chorus is the hook a song is remembered by, and opening with another section's phrase takes that away.", "The return of the verse's phrase is the song's idea."),
        ["chorus.below"] = new(S, D, "A chorus usually stands above the sections before it, where it arrives.", "A quiet, low chorus is the song's idea."),
        ["chorus.last-unchanged"] = new(I, L, "The last chorus is where a song arrives, and a change the listener hears makes it land.", "A plain repeat suits the style, as in folk song."),

        // The piano
        ["piano.hand-register"] = new(M, D, "A pianist's left hand lies round C3 and the right round C5. Far from there, an octave mark has often slipped.", "The register is the effect meant."),
        ["piano.hands-apart"] = new(S, D, "With the hands far apart the middle of the keyboard is empty, and the sound is thin and hollow.", "The hollow sound is the effect meant."),
        ["piano.left-reaches-melody"] = new(S, D, "A left-hand figure on the melody's own note or above it hides the melody.", "A hand crossing is written as a figure of its own."),
        ["piano.figure-wide"] = new(S, D, "A hand's figure usually stays within an octave and a half. Wider, it needs the pedal and a jump.", "A virtuoso texture is meant."),

        // Voicing and spelling
        ["voicing.low-chords"] = new(M, D, "Thirds and seconds below about C3 blur into a rumble, because low partials clash. Low voicings are spaced in fifths and octaves.", "A dark cluster is the effect meant."),
        ["voicing.thick-distorted"] = new(S, D, "Distortion multiplies a chord's overtones, so an electric guitar plays power chords where a clean one plays full shapes.", "A wall of noise is the effect meant."),
        ["octave.slip"] = new(M, D, "One note an octave or more away and straight back is rarely a tune's idea and often a misplaced octave mark.", "It is a wide grace or a stride figure, as a lead's accent."),
        ["key.signature"] = new(M, D, "A key signature saves writing the key's sharps or flats on every note. If the music sits in another key than the one written, the page fills with accidentals and the reader expects the wrong notes.", "The accidentals are the music's own: a mode, or a passage that has left the key."),
        ["key.outside"] = new(M, L, "A note outside the key usually leads somewhere, by a semitone into a note of the key, or belongs to a chord from outside it. One that does neither is often a slip.", "The style uses notes outside the key freely, as jazz and the blues do."),
        ["spelling.direction"] = new(N, D, "A note on its way up is written as the lower note raised (C♯), and one on its way down as the upper note lowered (D♭). The letter shows the direction, and the bar needs no natural.", "The note has a harmonic job that names it: it belongs to a chord, as the D♭ of a D♭ major chord."),
        ["spelling.key"] = new(N, H, "A note of the key is written by the key's own name. Under another name it reads as a note out of the key."),
        ["spelling.natural"] = new(N, H, "A note's name is its pitch, and the key never changes it. A natural is written by its natural name, with its natural sign where the key alters it."),

        // Form and length
        ["form.copied"] = new(N, H, "A section written twice note for note is one section named twice in the form, and a later fix then reaches both."),
        ["form.played-three-times"] = new(S, D, "A song's verses change their words, but an instrumental's returns repeat the same music unless they vary.", "A jazz head or a dance form repeats by design."),
        ["form.returns-unchanged"] = new(I, L, "A section that returns to end a piece usually lifts, and only louder is not yet new.", "A plain recapitulation suits the style."),
        ["form.length"] = new(I, L, "A piece needs room for its form to say something: phrases that close, and a return.", "A short piece was asked for: a sketch, a jingle, a study."),
        ["form.ends-in-another-key"] = new(M, D, "A piece usually closes in the key it began in, which is what makes the end sound like home.", "A progressive tonality or an open ending is meant."),
        ["form.final-bass"] = new(M, D, "A last chord over the tonic is what the ear hears as an ending.", "An open or inverted ending is meant."),
        ["form.ends-apart"] = new(S, D, "Parts that do not stop together leave the ending unresolved, since one is still running while the others hold.", "A fade or a solo tag is the ending."),
        ["texture.one-rhythm"] = new(S, D, "With every part in one rhythm, nothing stands out as a melody.", "The voices moving together is the music's character, as in a hymn or a chorale."),
    };
}
