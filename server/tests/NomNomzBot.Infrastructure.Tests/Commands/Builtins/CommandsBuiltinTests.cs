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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// <c>!commands</c> (legacy parity, S068b) must list REAL, currently-enabled triggers pulled from the same
/// two read paths the dashboard's Commands screen uses — not a hardcoded string. Also proves a disabled
/// command/built-in is excluded, so the listing reflects live state, not the full catalog, and that the
/// reply renders in the channel's personality tone (S069a) rather than a hardcoded string.
/// </summary>
public sealed class CommandsBuiltinTests
{
    private static BuiltinCommandContext Context(
        string personality = PersonalityTone.Informative,
        int roleLevel = 0,
        string commandPrefix = "!"
    ) =>
        new()
        {
            BroadcasterId = Guid.CreateVersion7(),
            TriggeringUserId = "42",
            TriggeringUserDisplayName = "Stoney_Eagle",
            TriggeringUserLogin = "stoney_eagle",
            Personality = personality,
            RoleLevel = roleLevel,
            CommandPrefix = commandPrefix,
        };

    private static IBuiltinResponseComposer FakeComposer()
    {
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string template = call.ArgAt<string>(0);
                foreach (
                    KeyValuePair<string, string> kvp in call.ArgAt<IDictionary<string, string>>(1)
                )
                    template = template.Replace($"{{{kvp.Key}}}", kvp.Value);
                return Task.FromResult(template);
            });
        return new BuiltinResponseComposer(
            resolver,
            NoPlatformBuiltinReplies.Instance,
            FakeChannelBuiltinReplies.None
        );
    }

    private static IServiceProvider FakeServiceProvider(IBuiltinCommandService builtins)
    {
        IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IBuiltinCommandService)).Returns(builtins);
        return serviceProvider;
    }

    private static CommandListItem FakeCommand(
        string name,
        bool isEnabled,
        string minPermissionLevel = "Everyone",
        string prefixMode = "Default",
        string? customPrefix = null,
        string matchMode = "StartsWith"
    ) =>
        new(
            Guid.CreateVersion7(),
            name,
            "template",
            minPermissionLevel,
            isEnabled,
            prefixMode,
            customPrefix,
            matchMode,
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
                    Dto("lurk", enabled: true, cooldown: 5),
                    Dto("accountage", enabled: true, cooldown: 15),
                ])
            );

        CommandsBuiltin builtin = new(commands, FakeServiceProvider(builtins), FakeComposer());

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
                    Dto("lurk", enabled: false, cooldown: 5),
                ])
            );

        CommandsBuiltin builtin = new(commands, FakeServiceProvider(builtins), FakeComposer());

        Result<string> result = await builtin.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("sr");
        result.Value.Should().NotContain("disabledcmd");
        result.Value.Should().NotContain("lurk");
    }

    [Fact]
    public async Task Sassy_tone_produces_the_sassy_variant_not_the_raw_hardcoded_string()
    {
        ICommandService commands = Substitute.For<ICommandService>();
        commands
            .ListAsync(Arg.Any<string>(), Arg.Any<PaginationParams>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new PagedList<CommandListItem>([FakeCommand("sr", isEnabled: true)], 1, 100, 1)
                )
            );
        IBuiltinCommandService builtins = Substitute.For<IBuiltinCommandService>();
        builtins
            .ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<BuiltinCommandDto>>([]));

        CommandsBuiltin builtin = new(commands, FakeServiceProvider(builtins), FakeComposer());

        Result<string> sassy = await builtin.ExecuteAsync(Context(PersonalityTone.Sassy));
        Result<string> informative = await builtin.ExecuteAsync(Context());

        string oldHardcodedString = "@Stoney_Eagle available commands: sr";
        sassy.Value.Should().NotBe(oldHardcodedString);
        HashSet<string> sassyVariants =
        [
            .. ToneTemplateCatalog
                .Get(
                    PersonalityTone.Sassy,
                    BuiltinResponseSlots.Commands.Key,
                    BuiltinResponseSlots.Commands.List
                )
                .Select(t =>
                    t.Replace("{user}", "Stoney_Eagle")
                        .Replace("{commands}", "!sr")
                        .Replace("{prefix}", "!")
                ),
        ];
        sassyVariants.Should().Contain(sassy.Value);

        // Default tone still reads exactly as it did before this slice (regression).
        informative.Value.Should().Be("!sr — Use !help <command> for details.");
    }

    private static BuiltinCommandDto Dto(
        string key,
        bool enabled,
        int cooldown,
        string floor = "Everyone",
        string? floorOverride = null
    ) => new(key, key, enabled, cooldown, floor, key, false, false, null, floorOverride, 0);

    private static CommandsBuiltin BuiltinOver(
        IReadOnlyList<CommandListItem> custom,
        IReadOnlyList<BuiltinCommandDto> builtinDtos
    )
    {
        ICommandService commands = Substitute.For<ICommandService>();
        commands
            .ListAsync(Arg.Any<string>(), Arg.Any<PaginationParams>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(new PagedList<CommandListItem>([.. custom], 1, 100, custom.Count))
            );
        IBuiltinCommandService builtins = Substitute.For<IBuiltinCommandService>();
        builtins
            .ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(builtinDtos));
        return new(commands, FakeServiceProvider(builtins), FakeComposer());
    }

    [Fact]
    public async Task A_viewer_sees_only_what_a_viewer_can_run_and_a_moderator_sees_the_rest_too()
    {
        CommandsBuiltin builtin = BuiltinOver(
            [
                FakeCommand("lurk", isEnabled: true),
                FakeCommand("raid", isEnabled: true, minPermissionLevel: "Moderator"),
            ],
            [
                Dto("uptime", enabled: true, cooldown: 5),
                Dto("title", enabled: true, cooldown: 5, floor: "Moderator"),
                Dto("followage", enabled: true, cooldown: 5, floorOverride: "Moderator"),
            ]
        );

        Result<string> viewer = await builtin.ExecuteAsync(Context(roleLevel: 0));
        Result<string> moderator = await builtin.ExecuteAsync(Context(roleLevel: 10));

        viewer.Value.Should().Be("!lurk, !uptime — Use !help <command> for details.");
        moderator
            .Value.Should()
            .Be("!followage, !lurk, !raid, !title, !uptime — Use !help <command> for details.");
    }

    [Fact]
    public async Task Each_command_is_shown_the_way_it_is_typed_and_regex_triggers_are_left_out()
    {
        CommandsBuiltin builtin = BuiltinOver(
            [
                FakeCommand("discord", isEnabled: true),
                FakeCommand("hug", isEnabled: true, prefixMode: "Custom", customPrefix: "#"),
                FakeCommand("hello", isEnabled: true, prefixMode: "None"),
                FakeCommand("pattern", isEnabled: true, matchMode: "Regex"),
            ],
            [Dto("uptime", enabled: true, cooldown: 5)]
        );

        Result<string> result = await builtin.ExecuteAsync(Context(commandPrefix: "?"));

        result
            .Value.Should()
            .Be("?discord, hello, #hug, ?uptime — Use ?help <command> for details.");
    }

    [Fact]
    public async Task Every_tone_carries_the_help_hint_and_the_same_command_list()
    {
        CommandsBuiltin builtin = BuiltinOver(
            [FakeCommand("sr", isEnabled: true)],
            [Dto("uptime", enabled: true, cooldown: 5)]
        );

        foreach (
            string tone in new[]
            {
                PersonalityTone.Informative,
                PersonalityTone.Friendly,
                PersonalityTone.Sassy,
                PersonalityTone.Hype,
                PersonalityTone.Chill,
            }
        )
        {
            Result<string> result = await builtin.ExecuteAsync(Context(tone));
            result.Value.Should().Contain("!sr, !uptime", tone);
            result.Value.Should().Contain("!help <command>", tone);
        }
    }

    [Fact]
    public async Task With_nothing_enabled_the_informative_reply_is_the_legacy_sentence()
    {
        CommandsBuiltin builtin = BuiltinOver([], []);

        Result<string> result = await builtin.ExecuteAsync(Context());

        result.Value.Should().Be("No commands available.");
    }
}
