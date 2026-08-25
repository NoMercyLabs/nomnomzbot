// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// <c>{args.1}</c> is the FIRST argument, on every path that seeds command arguments.
/// <para>
/// The two paths disagreed: chat commands seeded <c>args.0</c>-based while <c>run_pipeline</c> seeded
/// <c>args.1</c>-based, and every action's documented example said <c>{args.1}</c>. So a chat command
/// reading <c>{args.1}</c> silently received the SECOND word — <c>!permit @user</c> and the raid flow's
/// target both resolved to nothing — and a pipeline written against <c>{args.0}</c> broke the moment it
/// was called as a sub-pipeline. This is a source-level guard because the drift lives in two files that
/// no single runtime test spans, and it is invisible until a live command answers the wrong thing.
/// </para>
/// </summary>
public sealed class CommandArgumentIndexTests
{
    [Theory]
    [InlineData("Chat/EventHandlers/ChatMessageHandler.cs")]
    [InlineData("Platform/Pipeline/CoreActions/RunPipelineAction.cs")]
    [InlineData("Platform/Pipeline/PipelineEngine.cs")]
    public void Every_path_that_seeds_command_arguments_is_one_based(string relativePath)
    {
        string source = ReadInfrastructureSource(relativePath);

        MatchCollection seeds = Regex.Matches(source, @"""args\.\{(?<expr>[^}]+)\}""");
        seeds.Should().NotBeEmpty($"{relativePath} is supposed to seed indexed command arguments");

        foreach (Match seed in seeds)
        {
            string expression = seed.Groups["expr"].Value.Replace(" ", string.Empty);
            expression
                .Should()
                .Be(
                    "i+1",
                    $"{relativePath} seeds args.{{{seed.Groups["expr"].Value}}} — a bare loop index makes "
                        + "{args.1} the second word, so the first argument of every command is lost"
                );
        }
    }

    [Fact]
    public void No_action_reads_the_zero_index_that_no_path_seeds_any_more()
    {
        string infrastructure = Path.Combine(
            RepoRoot(),
            "server",
            "src",
            "NomNomzBot.Infrastructure"
        );
        List<string> offenders =
        [
            .. Directory
                .EnumerateFiles(infrastructure, "*.cs", SearchOption.AllDirectories)
                .Where(file => File.ReadAllText(file).Contains("args.0", StringComparison.Ordinal))
                .Select(file => Path.GetRelativePath(infrastructure, file)),
        ];

        offenders
            .Should()
            .BeEmpty(
                "nothing seeds args.0 any longer, so every read of it — in code OR in a documented "
                    + "example a streamer will copy — resolves to nothing"
            );
    }

    private static string ReadInfrastructureSource(string relativePath) =>
        File.ReadAllText(
            Path.Combine(RepoRoot(), "server", "src", "NomNomzBot.Infrastructure", relativePath)
        );

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "server", "src")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root from {AppContext.BaseDirectory}."
        );
    }
}
