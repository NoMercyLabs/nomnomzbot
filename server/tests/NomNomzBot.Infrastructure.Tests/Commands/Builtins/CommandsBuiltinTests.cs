// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// <c>!commands</c> (legacy parity, S068b) must list REAL, currently-enabled triggers pulled from the same
/// two read paths the dashboard's Commands screen uses — not a hardcoded string. Also proves a disabled
/// command/built-in is excluded, so the listing reflects live state, not the full catalog.
/// </summary>
public sealed class CommandsBuiltinTests
{
    private static BuiltinCommandContext Context() =>
        new()
        {
            BroadcasterId = Guid.CreateVersion7(),
            TriggeringUserId = "42",
            TriggeringUserDisplayName = "Stoney_Eagle",
            TriggeringUserLogin = "stoney_eagle",
        };

    private static CommandListItem FakeCommand(string name, bool isEnabled) =>
        new(
            Guid.CreateVersion7(),
            name,
            "template",
            0,
            isEnabled,
            "Default",
            null,
            "StartsWith",
            null,
            0,
            0,
            false,
            null,
            [],
            0,
            DateTime.UtcNow,
            "hi",
            null,
            null
        );

    [Fact]
    public async Task Lists_both_enabled_custom_commands_and_enabled_builtins_from_the_real_queries()
    {
        ICommandService commands = Substitute.For<ICommandService>();
        commands
            .ListAsync(Arg.Any<string>(), Arg.Any<PaginationParams>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new PagedList<CommandListItem>(
                        [FakeCommand("sr", isEnabled: true), FakeCommand("hug", isEnabled: true)],
                        1,
                        100,
                        2
                    )
                )
            );

        IBuiltinCommandService builtins = Substitute.For<IBuiltinCommandService>();
        builtins
            .ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<BuiltinCommandDto>>([
                    new BuiltinCommandDto("lurk", "lurk", true, 5, 0),
                    new BuiltinCommandDto("accountage", "accountage", true, 15, 0),
                ])
            );

        CommandsBuiltin builtin = new(commands, builtins);

        Result<string> result = await builtin.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("sr");
        result.Value.Should().Contain("hug");
        result.Value.Should().Contain("lurk");
        result.Value.Should().Contain("accountage");
    }

    [Fact]
    public async Task Excludes_disabled_commands_and_builtins_from_the_listing()
    {
        ICommandService commands = Substitute.For<ICommandService>();
        commands
            .ListAsync(Arg.Any<string>(), Arg.Any<PaginationParams>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new PagedList<CommandListItem>(
                        [
                            FakeCommand("sr", isEnabled: true),
                            FakeCommand("disabledcmd", isEnabled: false),
                        ],
                        1,
                        100,
                        2
                    )
                )
            );

        IBuiltinCommandService builtins = Substitute.For<IBuiltinCommandService>();
        builtins
            .ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<BuiltinCommandDto>>([
                    new BuiltinCommandDto("lurk", "lurk", false, 5, 0),
                ])
            );

        CommandsBuiltin builtin = new(commands, builtins);

        Result<string> result = await builtin.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("sr");
        result.Value.Should().NotContain("disabledcmd");
        result.Value.Should().NotContain("lurk");
    }
}
