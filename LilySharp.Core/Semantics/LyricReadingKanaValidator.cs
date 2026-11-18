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
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// LYS4037: a reading (<c>漢[kan]</c>) that is not kana, in words written in JAPANESE — the
/// Japanese voices sing kana. In another language a reading is what that language's voices read:
/// pinyin for a Han character read another way than its customary one (<c>长[chang]</c>), the
/// sounded syllable for a Hangul one (<c>넘[너]</c>), and no warning.
/// </summary>
/// <remarks>
/// ⚠️ THE PARSER SAID THIS until 2026-11-18, of every reading, whatever the words' language:
/// the first songs of the singing walkthrough (owner's report) warned "a Japanese voice sings
/// hiragana or katakana" at <c>长[chang]</c> under <c>lyrics words language chinese</c>. The
/// parser cannot know the language — a track's is the first block of its name that writes one,
/// else the file's last <c>language</c> line (<see cref="LyricLanguage.ReaderOf"/>), either of
/// which may stand after the syllable — so the check is here, where the whole tree is read.
/// The malformed brackets (LYS4036) stay the parser's: they are a matter of the syllable alone.
/// </remarks>
internal sealed class LyricReadingKanaValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        System.Func<string?, string>? languageOf = null;
        foreach (var block in root.DescendantNodes<LyricsBlockSyntax>())
        {
            bool? japanese = null;      // asked only of a block that writes a reading
            foreach (var node in block.DescendantNodes())
            {
                if (node.Kind != SyntaxKind.LyricSyllable || node.GetChild(0) is not SyntaxTokenNode token)
                    continue;
                string raw = token.Text;
                if (!LyricReading.HasBrackets(raw) || StringLiteral.IsClosed(raw) || LyricReading.Split(raw).Problem != null)
                    continue;
                japanese ??= (languageOf ??= LyricLanguage.ReaderOf(root))(block.VoiceName) == LyricLanguage.DefaultCode;
                if (japanese != true)
                    break;
                foreach (var reading in LyricReading.Readings(raw))
                    if (!LyricReading.IsKana(reading))
                    {
                        _diagnostics.Warning(token.Span, DiagnosticCodes.LyricReadingNotKana,
                            $"the reading [{reading}] is not kana — a Japanese voice sings hiragana or katakana; "
                            + "the word still prints and the reading is handed to the voice as written");
                        break;
                    }
            }
        }
    }
}
