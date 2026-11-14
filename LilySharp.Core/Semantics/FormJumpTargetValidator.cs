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
using System.Linq;
using System.Text;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Warns (LYS4025) at a jump text whose landmark nothing writes where the route looks for it:
/// a <c>ds</c> with no <c>segno</c> before it, an <c>al fine</c> with no <c>fine</c> on the
/// replayed stretch, an <c>al coda</c> with no <c>to coda</c> on it or no <c>coda</c> after the
/// jump. Each message names the fallback the MIDI takes.
/// </summary>
/// <remarks>
/// <para>
/// The faults are <see cref="FormRoute"/>'s own:
/// <see cref="FormRoute.Of(IReadOnlyList{FormWalk.Item}, List{FormRoute.Fault})"/> appends one as it
/// takes each fallback, on the same walk whose stretches the MIDI plays, so this validator
/// spells no rule of its own and cannot drift from the sound. Forms are read wherever they
/// stand (one per scope, <see cref="FormDeclarationValidator"/>'s rule); a file with no
/// navigation mark at all is left before any form is walked.
/// </para>
/// <para>
/// A book that writes a sign in its MUSIC is read on the score's column instead
/// (<see cref="RepeatStream.JumpFaults"/>, session 1249): there a mark of the music and a mark
/// of the form are one set, so a form's <c>ds</c> finds the <c>segno</c> a section's music
/// writes, and a <c>ds</c> in the music is held to the same landmarks. Until LYS1034 narrowed
/// to the phrase that book was an error and nobody asked; the form's walk alone would have
/// said "no segno" to a writer looking at one.
/// </para>
/// </remarks>
internal sealed class FormJumpTargetValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        if (!root.DescendantNodes<NavigationMarkSyntax>().Any())
            return;

        if (RepeatStream.InTheMusic(root))
        {
            var forms = new List<FormDeclarationSyntax?>(TopLevelNodes.OfRoot<FormDeclarationSyntax>(root));
            if (forms.Count == 0)
                forms.Add(null);
            var said = new HashSet<(int, FormRoute.JumpFault)>();
            foreach (var form in forms)
                foreach (var fault in RepeatStream.For(root, form).JumpFaults)
                    if (said.Add((fault.Jump.Span.Start, fault.Kind)))
                        _diagnostics.Warning(fault.Jump.Span, DiagnosticCodes.JumpTargetMissing,
                            Message(fault.Jump, fault.Kind, where: ""));
            return;
        }

        var faults = new List<FormRoute.Fault>();
        foreach (var form in root.DescendantNodes<FormDeclarationSyntax>())
        {
            faults.Clear();
            FormRoute.Of(FormWalk.Read(form), faults);
            foreach (var fault in faults)
                _diagnostics.Warning(fault.Jump.Span, DiagnosticCodes.JumpTargetMissing, Message(fault.Jump, fault.Kind, where: " in the form"));
        }
    }

    /// <param name="where">Where the missing landmark is to be written: " in the form" for a book whose
    /// signs are the form's alone, nothing for one whose music writes them too (either place will do).</param>
    private static string Message(NavigationMarkSyntax mark, FormRoute.JumpFault kind, string where)
    {
        string jump = Written(mark);
        return kind switch
        {
            FormRoute.JumpFault.NoSegno =>
                $"'{jump}' has no 'segno' before it, so the jump is not followed (the mark is "
                + $"printed; the MIDI plays on). Write 'segno'{where} where the replay should begin.",
            FormRoute.JumpFault.NoFine =>
                $"'{jump}' finds no 'fine' on the stretch it replays, so the replay runs to the jump "
                + $"itself and the piece ends there. Write 'fine'{where} where the piece should end.",
            FormRoute.JumpFault.NoToCoda =>
                $"'{jump}' finds no 'to coda' on the stretch it replays, so the replay runs to the jump "
                + $"itself before going to the coda. Write 'to coda'{where} where the replay should leave for the coda.",
            FormRoute.JumpFault.NoCoda =>
                $"'{jump}' finds no 'coda' after it, so after the replay the piece resumes right after "
                + $"the jump. Write 'coda'{where} where the coda begins.",
            _ => $"'{jump}' asks for a landmark nothing writes.",
        };
    }

    /// <summary>The jump text as the writer spelled it (<c>ds al coda</c>), one space between
    /// its words.</summary>
    private static string Written(NavigationMarkSyntax mark)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < mark.SlotCount; i++)
        {
            if (mark.GetChild(i) is SyntaxTokenNode token)
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(token.Text);
            }
        }
        return sb.ToString();
    }
}
