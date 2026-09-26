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

using LilySharp.Cli;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Unit tests for the unified CLI option parser that replaced the four divergent
/// ad-hoc parsers. Pins the strict-validation behavior (unknown option / stray
/// positional now rejected — the fix for check/harmonize/layout) and the exact
/// legacy error messages.
/// </summary>
public class CliParserTests
{
    private static CliParser OutputStyle() => new CliParser(maxPositionals: 2)
        .Value("output", "-o requires a file path", "-o", "--output")
        .Value("score", "--score requires a score name", "--score")
        .Flag("all", "--all");

    [Fact]
    public void Value_option_captures_following_token()
    {
        var r = OutputStyle().Parse(new[] { "in.lys", "-o", "out.svg" });
        Assert.Null(r.Error);
        Assert.Equal("out.svg", r.Get("output"));
        Assert.Equal(new[] { "in.lys" }, r.Positionals);
    }

    [Fact]
    public void Long_and_short_aliases_map_to_one_canonical()
    {
        Assert.Equal("x", OutputStyle().Parse(new[] { "-o", "x" }).Get("output"));
        Assert.Equal("x", OutputStyle().Parse(new[] { "--output", "x" }).Get("output"));
    }

    [Theory]
    [InlineData("--verbose")]
    [InlineData("--debug")]
    public void TakeVerbose_strips_flag_and_records_it(string flag)
    {
        var args = new[] { "svg", flag, "in.lys" };
        Assert.True(CliParser.TakeVerbose(ref args));
        Assert.True(CliParser.Verbose);
        Assert.Equal(new[] { "svg", "in.lys" }, args); // flag stripped so strict parse won't reject it

        var plain = new[] { "svg", "in.lys" };
        Assert.False(CliParser.TakeVerbose(ref plain));
        Assert.False(CliParser.Verbose);
        Assert.Equal(new[] { "svg", "in.lys" }, plain);
    }

    [Fact]
    public void Flag_is_present_without_a_value()
    {
        var r = OutputStyle().Parse(new[] { "in.lys", "--all" });
        Assert.Null(r.Error);
        Assert.True(r.Has("all"));
    }

    [Fact]
    public void Missing_value_uses_the_per_option_message()
    {
        Assert.Equal("-o requires a file path", OutputStyle().Parse(new[] { "in.lys", "-o" }).Error);
        Assert.Equal("--score requires a score name", OutputStyle().Parse(new[] { "in.lys", "--score" }).Error);
    }

    [Fact]
    public void Unknown_option_is_rejected()
    {
        // This is the strictness check/harmonize/layout previously LACKED.
        Assert.Equal("Unknown option: --bogus", OutputStyle().Parse(new[] { "in.lys", "--bogus" }).Error);
    }

    [Fact]
    public void Positional_beyond_the_max_is_rejected()
    {
        var r = new CliParser(maxPositionals: 1).Parse(new[] { "a.lys", "b.lys" });
        Assert.Equal("Unexpected argument: b.lys", r.Error);
    }
}
