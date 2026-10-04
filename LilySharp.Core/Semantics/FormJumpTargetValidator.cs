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
/// Warns (LYS4025) at a form's jump text whose landmark the form does not write where the
/// route looks for it: a <c>ds</c> with no <c>segno</c> before it, an <c>al fine</c> with no
/// <c>fine</c> on the replayed stretch, an <c>al coda</c> with no <c>to coda</c> on it or no
/// <c>coda</c> after the jump. Each message names the fallback the MIDI takes.
/// </summary>
/// <remarks>
/// The faults are <see cref="FormRoute"/>'s own:
/// <see cref="FormRoute.Of(IReadOnlyList{FormWalk.Item}, List{FormRoute.Fault})"/> appends one as it
/// takes each fallback, on the same walk whose stretches the MIDI plays, so this validator
/// spells no rule of its own and cannot drift from the sound. Forms are read wherever they
/// stand (one per scope, <see cref="FormDeclarationValidator"/>'s rule); a file with no
/// navigation mark at all is left before any form is walked.
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

        var faults = new List<FormRoute.Fault>();
        foreach (var form in root.DescendantNodes<FormDeclarationSyntax>())
        {
            faults.Clear();
            FormRoute.Of(FormWalk.Read(form), faults);
            foreach (var fault in faults)
                _diagnostics.Warning(fault.Jump.Span, DiagnosticCodes.JumpTargetMissing, Message(fault));
        }
    }

    private static string Message(FormRoute.Fault fault)
    {
        string jump = Written(fault.Jump);
        return fault.Kind switch
        {
            FormRoute.JumpFault.NoSegno =>
                $"'{jump}' has no 'segno' before it, so the jump is not followed (the mark is "
                + "printed; the MIDI plays on). Write 'segno' in the form where the replay should begin.",
            FormRoute.JumpFault.NoFine =>
                $"'{jump}' finds no 'fine' on the stretch it replays, so the replay runs to the jump "
                + "itself and the piece ends there. Write 'fine' in the form where the piece should end.",
            FormRoute.JumpFault.NoToCoda =>
                $"'{jump}' finds no 'to coda' on the stretch it replays, so the replay runs to the jump "
                + "itself before going to the coda. Write 'to coda' in the form where the replay should leave for the coda.",
            FormRoute.JumpFault.NoCoda =>
                $"'{jump}' finds no 'coda' after it, so after the replay the piece resumes right after "
                + "the jump. Write 'coda' in the form where the coda begins.",
            _ => $"'{jump}' asks for a landmark the form does not write.",
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
