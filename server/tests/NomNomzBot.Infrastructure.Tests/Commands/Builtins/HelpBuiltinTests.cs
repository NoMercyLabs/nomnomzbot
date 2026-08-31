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
/// <c>!help</c> (legacy parity, S068b) with a command name must answer from that command's REAL, queried
/// <see cref="CommandDto.Description"/> — not a hardcoded string — and fall back sanely (to the generic
/// enabled-trigger listing) for an unknown name. Both branches render in the channel's personality tone
/// (S069a).
/// </summary>
public sealed class HelpBuiltinTests
{
    private static BuiltinCommandContext Context(
        string args = "",
        string personality = PersonalityTone.Informative
    ) =>
        new()
        {
            BroadcasterId = Guid.CreateVersion7(),
            TriggeringUserId = "42",
            TriggeringUserDisplayName = "Stoney_Eagle",
            TriggeringUserLogin = "stoney_eagle",
            Args = args,
            Personality = personality,
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
        return new BuiltinResponseComposer(resolver);
    }

    private static CommandDto FakeCommand(string name, string? description) =>
        new(
            Guid.CreateVersion7(),
            name,
            "template",
            0,
            true,
            "Default",
            null,
            "StartsWith",
            null,
            "hi",
            null,
            null,
            0,
            0,
            false,
            description,
            [],
            0,
            DateTime.UtcNow,
            DateTime.UtcNow
        );

    private static IBuiltinCommandService EmptyBuiltins()
    {
        IBuiltinCommandService builtins = Substitute.For<IBuiltinCommandService>();
        builtins
            .ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<BuiltinCommandDto>>([]));
        return builtins;
    }

    [Fact]
    public async Task Help_with_a_known_command_name_replies_with_its_real_description()
    {
        ICommandService commands = Substitute.For<ICommandService>();
        commands
            .GetAsync(Arg.Any<string>(), "sr", Arg.Any<CancellationToken>())
            .Returns(Result.Success(FakeCommand("sr", "Request a song by title or link.")));

        HelpBuiltin help = new(
            commands,
            new CommandsBuiltin(commands, EmptyBuiltins(), FakeComposer()),
            FakeComposer()
        );

        Result<string> result = await help.ExecuteAsync(Context("sr"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Request a song by title or link.");
    }

    [Fact]
    public async Task Help_with_an_unknown_command_name_falls_back_to_the_generic_listing()
    {
        ICommandService commands = Substitute.For<ICommandService>();
        commands
            .GetAsync(Arg.Any<string>(), "nosuchcommand", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CommandDto>("Command not found.", "NOT_FOUND"));
        commands
            .ListAsync(Arg.Any<string>(), Arg.Any<PaginationParams>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new PagedList<CommandListItem>(
                        [
                            new CommandListItem(
                                Guid.CreateVersion7(),
                                "hug",
                                "template",
                                0,
                                true,
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
                                "hug msg",
                                null,
                                null
                            ),
                        ],
                        1,
                        100,
                        1
                    )
                )
            );

        HelpBuiltin help = new(
            commands,
            new CommandsBuiltin(commands, EmptyBuiltins(), FakeComposer()),
            FakeComposer()
        );

        Result<string> result = await help.ExecuteAsync(Context("nosuchcommand"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("hug");
    }

    [Fact]
    public async Task Help_with_no_argument_replies_with_the_generic_listing()
    {
        ICommandService commands = Substitute.For<ICommandService>();
        commands
            .ListAsync(Arg.Any<string>(), Arg.Any<PaginationParams>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new PagedList<CommandListItem>(
                        [
                            new CommandListItem(
                                Guid.CreateVersion7(),
                                "hug",
                                "template",
                                0,
                                true,
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
                                "hug msg",
                                null,
                                null
                            ),
                        ],
                        1,
                        100,
                        1
                    )
                )
            );

        HelpBuiltin help = new(
            commands,
            new CommandsBuiltin(commands, EmptyBuiltins(), FakeComposer()),
            FakeComposer()
        );

        Result<string> result = await help.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("hug");
    }

    [Fact]
    public async Task Sassy_tone_produces_the_sassy_described_variant_not_the_raw_hardcoded_string()
    {
        ICommandService commands = Substitute.For<ICommandService>();
        commands
            .GetAsync(Arg.Any<string>(), "sr", Arg.Any<CancellationToken>())
            .Returns(Result.Success(FakeCommand("sr", "Request a song by title or link.")));

        HelpBuiltin help = new(
            commands,
            new CommandsBuiltin(commands, EmptyBuiltins(), FakeComposer()),
            FakeComposer()
        );

        Result<string> sassy = await help.ExecuteAsync(
            Context("sr", personality: PersonalityTone.Sassy)
        );
        Result<string> informative = await help.ExecuteAsync(
            Context("sr", personality: PersonalityTone.Informative)
        );

        string oldHardcodedString = "@Stoney_Eagle !sr: Request a song by title or link.";
        sassy.Value.Should().NotBe(oldHardcodedString);
        HashSet<string> sassyVariants =
        [
            .. ToneTemplateCatalog
                .Get(
                    PersonalityTone.Sassy,
                    BuiltinResponseSlots.Help.Key,
                    BuiltinResponseSlots.Help.Described
                )
                .Select(t =>
                    t.Replace("{user}", "Stoney_Eagle")
                        .Replace("{command}", "sr")
                        .Replace("{description}", "Request a song by title or link.")
                ),
        ];
        sassyVariants.Should().Contain(sassy.Value);

        // Default tone still reads exactly as it did before this slice (regression).
        informative.Value.Should().Be(oldHardcodedString);
    }
}
