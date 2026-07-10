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

namespace LilySharp.Cli;

/// <summary>
/// One strict command-line option parser shared by every subcommand, replacing
/// the four divergent ad-hoc parsers (ParseSvgOptions, ParseSimpleOptions, the
/// inline RunPng loop, and the lax <c>args.FirstOrDefault(a =&gt; !a.StartsWith('-'))</c>
/// in check/harmonize/layout). Handles value options (<c>-o &lt;v&gt;</c>), boolean flags
/// (<c>--all</c>), and positionals, rejecting unknown options and stray positionals
/// uniformly — so check/harmonize/layout now validate like svg/pdf/… already did.
/// Input-file presence/existence resolution is <see cref="ResolveIo"/> (output
/// commands) or per-command (check/harmonize/layout keep their own messages).
/// </summary>
internal sealed class CliParser
{
    // alias -> (canonical name, message shown when the option's value is missing)
    private readonly Dictionary<string, (string Canon, string MissingMsg)> _valueOpts = new();
    // alias -> canonical name
    private readonly Dictionary<string, string> _boolFlags = new();
    private readonly int _maxPositionals;

    public CliParser(int maxPositionals) => _maxPositionals = maxPositionals;

    /// <summary>Declares a value-taking option. <paramref name="missingMsg"/> is emitted
    /// verbatim when the option is given without a following value (matching the old
    /// per-option messages, e.g. "-o requires a file path").</summary>
    public CliParser Value(string canonical, string missingMsg, params string[] aliases)
    {
        foreach (var a in aliases) _valueOpts[a] = (canonical, missingMsg);
        return this;
    }

    /// <summary>Declares a boolean flag (no value).</summary>
    public CliParser Flag(string canonical, params string[] aliases)
    {
        foreach (var a in aliases) _boolFlags[a] = canonical;
        return this;
    }

    internal sealed class Result
    {
        public List<string> Positionals { get; } = new();
        public Dictionary<string, string> Values { get; } = new();
        public HashSet<string> Flags { get; } = new();
        public string? Error { get; set; }

        public bool Has(string canonical) => Flags.Contains(canonical);
        public string? Get(string canonical) => Values.TryGetValue(canonical, out var v) ? v : null;
    }

    /// <summary>
    /// Parses <paramref name="args"/> strictly: an unknown <c>-</c>-prefixed token,
    /// a missing option value, or a positional beyond the declared maximum yields
    /// <see cref="Result.Error"/> (with the same messages the old parsers used).
    /// </summary>
    public Result Parse(string[] args)
    {
        var r = new Result();
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (_valueOpts.TryGetValue(arg, out var vo))
            {
                if (i + 1 >= args.Length) { r.Error = vo.MissingMsg; return r; }
                r.Values[vo.Canon] = args[++i];
            }
            else if (_boolFlags.TryGetValue(arg, out var fcanon))
            {
                r.Flags.Add(fcanon);
            }
            else if (arg.StartsWith("-"))
            {
                r.Error = $"Unknown option: {arg}";
                return r;
            }
            else if (r.Positionals.Count < _maxPositionals)
            {
                r.Positionals.Add(arg);
            }
            else
            {
                r.Error = $"Unexpected argument: {arg}";
                return r;
            }
        }
        return r;
    }

    /// <summary>
    /// Resolves the standard input/output pair for the file-output commands from a
    /// parsed result: positional[0] is the input (must exist); the output is the
    /// <c>-o/--output</c> value, else positional[1], else the input with
    /// <paramref name="defaultExt"/>. Reproduces the old ParseSimpleOptions/ParseSvgOptions
    /// messages exactly, including rejecting a stray positional when <c>-o</c> already set
    /// the output ("Unexpected argument: …").
    /// </summary>
    public static (string? Input, string? Output, string? Error) ResolveIo(Result r, string defaultExt)
    {
        if (r.Positionals.Count == 0)
            return (null, null, "Input file required");

        string input = r.Positionals[0];
        if (!File.Exists(input))
            return (null, null, $"File not found: {input}");

        string? explicitOutput = r.Get("output");
        if (explicitOutput != null && r.Positionals.Count >= 2)
            // -o AND a second positional: the old loop hit the second positional with
            // outputPath already set and rejected it.
            return (null, null, $"Unexpected argument: {r.Positionals[1]}");

        string output = explicitOutput
            ?? (r.Positionals.Count >= 2 ? r.Positionals[1] : Path.ChangeExtension(input, defaultExt));
        return (input, output, null);
    }
}
