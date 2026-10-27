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

namespace LilySharp.Tests;

/// <summary>
/// Three short pieces that pass the composition check — a string quartet, a band, a jazz lead sheet.
/// They were the grammar's examples for "Compose a piece" until 2026-10-27, when the owner found the
/// AI composed to their size (8 bars, a tempo per section) and asked that the grammar show none.
/// </summary>
internal static class ComposedPieces
{
    public const string LittleQuartet = """
        title "Little Quartet"
        time 4/4
        key d minor
        octave absolute

        part vn1 "Violin I" "Vn. I" { instrument violin }
        part vn2 "Violin II" "Vn. II" { instrument violin }
        part va "Viola" "Va." { instrument viola }
        part vc "Cello" "Vc." { instrument cello }

        section A {
          tempo "Andante" 4 = 72
          vn1 { a'2@p( d''4 e'' | f''4. e''8 d''4 a') | bes'2( a'4 g' | a'1) | }
          vn2 { r8 f'@p a' f' r a' d'' a' | r8 f' a' f' r a' d'' a' | r8 g' bes' g' r bes' d'' bes' | r8 e' g' cis'' e''4 cis'' | }
          va { d'2@p f' | d'2 f' | d'2 g' | e'1 | }
          vc { d1@p | d1 | g,1 | a,1 | }
        }

        section B {
          key f major
          tempo "Allegro" 4 = 132
          vn1 { c''8@f@staccato c'' f'' c'' a'4@accent g' | f'8@staccato a' c'' f'' e''4@accent c'' | d''8@cresc e'' f'' g'' a''4 g'' | f''2@ff r2 | }
          vn2 { a'8@f@staccato a' c'' a' f'4 e' | c'8@staccato f' a' c'' g'4 e' | f'8@cresc g' a' bes' c''4 bes' | a'2@ff r2 | }
          va { f'4@f@staccato r c'@staccato r | a4@staccato r c'@staccato r | bes4@cresc d' f' e' | f'2@ff r2 | }
          vc { f,4@f@staccato c@staccato f,@staccato c@staccato | f,4@staccato c@staccato c,@staccato c@staccato | bes,4@cresc bes, c c | f,2@ff r2 | }
        }

        form { A B }

        score {
          staffGroup { staff vn1  staff vn2  staff va  staff vc }
        }
        """;

    public const string NightDrive = """
        title "Night Drive"
        tempo 112
        time 4/4
        key e minor
        octave absolute

        part gtr "Electric Guitar" "E.Gt." { instrument guitar midiInstrument "overdriven guitar" }
        part keys "Organ" "Org." { instrument piano-right midiInstrument "rock organ" }
        part bass "Electric Bass" "E.Bass" { instrument bass midiInstrument "electric bass (finger)" }
        part kit "Drums" "Dr." { clef percussion }

        section Verse {
          gtr { <e, b,>8@mf r <e, b,> r <e, b,>4 r8 <e, b,> | <c g>8 r <c g> r <c g>4 r8 <c g> | <g, d>8 r <g, d> r <g, d>4 r8 <g, d> | <d a>8 r <d a> r <d a>4 <d a>@accent | }
          keys { <e' g' b'>1@p | <e' g' c''>1 | <d' g' b'>1 | <d' fis' a'>1 | }
          bass { e8@mf e e e e e b, d | c8 c c c c c g, b, | g,8 g, g, g, g, g, d fis | d8 d d d d4 d8 fis | }
          kit {
            voice { hh8@mf hh hh hh hh hh hh hh | hh8 hh hh hh hh hh hh hh | hh8 hh hh hh hh hh hh hh | hh8 hh hh hh sn16 sn sn sn tomh8 toml | }
                  { bd4 sn bd8 bd sn4 | bd4 sn bd8 bd sn4 | bd4 sn bd8 bd sn4 | bd4 sn bd r | }
          }
        }

        section Chorus {
          gtr { <g, d g>4@f@accent <g, d g>8 <g, d g> r <g, d g> <g, d g>4 | <d a d'>4@accent <d a d'>8 <d a d'> r <d a d'> <d a d'>4 | <e, b, e>4@accent <e, b, e>8 <e, b, e> r <e, b, e> <e, b, e>4 | <c g c'>4@accent <c g c'>8 <c g c'> <d a d'>2 | }
          keys { <g' b' d''>2@mf <g' b' d''> | <fis' a' d''>2 <fis' a' d''> | <e' g' b'>2 <e' g' b'> | <e' g' c''>2 <fis' a' d''> | }
          bass { g,8@f g, g, g, g, g, g, a, | d8 d d d d d d e | e8 e e e e e e d | c8 c c c d4 d | }
          kit {
            voice { cymc8@f hh hh hh hh hh hh hh | hh8 hh hh hh hh hh hh hh | hh8 hh hh hh hh hh hh hh | cymc4 hh8 hh sn8 sn tomh toml | }
                  { bd4 sn bd8 bd sn4 | bd4 sn bd8 bd sn4 | bd4 sn bd8 bd sn4 | bd4 r bd bd | }
          }
        }

        form { Verse Chorus }

        score {
          staff gtr
          staff keys
          staff bass
          staff kit
        }
        """;

    public const string BlueWindow = """
        title "Blue Window"
        tempo "Medium Swing" 4 = 152 swing
        time 4/4
        key f major
        octave absolute

        part lead { clef treble }

        section A1 {
          lead { r4 r8 c''8 f'' a' c'' e''~ | e''4 d''8 c'' bes'4 g'8 e' | g'4. a'8 c''4 a'8 es'' | d''4 bes'8 g' f'4 e'8 g'~ | g'2 r8 a' c'' e'' | des''4 c''8 bes' g'4 f'8 des'' | c''4 a'8 g' fis'4 a'8 c'' | bes'2 r4 r8 c'' | }
          chords changes { Fmaj7 | Gm7 C7 | Am7 D7(b9) | Gm7 C7 | Fmaj7 | Bbm7 Eb7 | Am7 D7 | Gm7 C7 | }
        }

        section A2 {
          lead { a'4 f'8 a' c''4 e''8 d''~ | d''4 bes'8 g' e'4 g'8 bes' | a'4. c''8 es''4 d''8 c'' | bes'4 g'8 d'' c''4 bes'8 g' | a'2. r8 a' | bes'4 des''8 f'' g''4 des''8 c'' | bes'4 g'8 bes' e'4 g'8 e' | f'1 | }
          chords changes { Fmaj7 | Gm7 C7 | Am7 D7(b9) | Gm7 C7 | Fmaj7 | Bbm7 Eb7 | Gm7 C7 | Fmaj7 | }
        }

        form { A1 A2 }

        score {
          chords changes
          staff lead
        }
        """;
}
