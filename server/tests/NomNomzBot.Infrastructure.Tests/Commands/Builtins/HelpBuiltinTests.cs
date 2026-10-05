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
        string personality = PersonalityTone.Informative,
        string prefix = "!"
    ) =>
        new()
        {
            BroadcasterId = Guid.CreateVersion7(),
            TriggeringUserId = "42",
            TriggeringUserDisplayName = "Stoney_Eagle",
            TriggeringUserLogin = "stoney_eagle",
            Args = args,
            Personality = personality,
            CommandPrefix = prefix,
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

    private static CommandDto FakeCommand(string name, string? description) =>
        new(
            Guid.CreateVersion7(),
            name,
            "template",
            "Everyone",
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

    private static IServiceProvider EmptyBuiltins()
    {
        IBuiltinCommandService builtins = Substitute.For<IBuiltinCommandService>();
        builtins
            .ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<BuiltinCommandDto>>([]));

        IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IBuiltinCommandService)).Returns(builtins);
        return serviceProvider;
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

        string oldHardcodedString = "!sr — Request a song by title or link.";
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

        informative.Value.Should().Be(oldHardcodedString);
    }

    private static HelpBuiltin HelpOver(ICommandService commands, params string[] builtinKeys)
    {
        IBuiltinCommandService builtins = Substitute.For<IBuiltinCommandService>();
        builtins
            .ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<BuiltinCommandDto>>([
                    .. builtinKeys.Select(k => new BuiltinCommandDto(
                        k,
                        k,
                        true,
                        5,
                        "Everyone",
                        k,
                        false,
                        false,
                        null,
                        null,
                        0
                    )),
                ])
            );
        IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IBuiltinCommandService)).Returns(builtins);
        commands
            .ListAsync(Arg.Any<string>(), Arg.Any<PaginationParams>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PagedList<CommandListItem>([], 1, 100, 0)));
        return new(
            commands,
            new CommandsBuiltin(commands, serviceProvider, FakeComposer()),
            FakeComposer()
        );
    }

    [Fact]
    public async Task Help_with_no_argument_replies_with_the_legacy_usage_line()
    {
        HelpBuiltin help = HelpOver(Substitute.For<ICommandService>(), "uptime");

        Result<string> result = await help.ExecuteAsync(Context());

        result
            .Value.Should()
            .Be(
                "Use !help <command> to get help for a specific command, or !commands to see what's available."
            );
    }

    [Fact]
    public async Task Help_with_an_unknown_name_replies_with_the_legacy_unknown_line_and_not_the_list()
    {
        ICommandService commands = Substitute.For<ICommandService>();
        commands
            .GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CommandDto>("Command not found.", "NOT_FOUND"));
        HelpBuiltin help = HelpOver(commands, "uptime");

        Result<string> result = await help.ExecuteAsync(Context("!NoSuch"));

        result
            .Value.Should()
            .Be("Unknown command \"nosuch\". Use !commands to see what's available.");
    }

    [Fact]
    public async Task Help_for_a_command_without_a_description_says_so_and_does_not_list_commands()
    {
        ICommandService commands = Substitute.For<ICommandService>();
        commands
            .GetAsync(Arg.Any<string>(), "hug", Arg.Any<CancellationToken>())
            .Returns(Result.Success(FakeCommand("hug", null)));
        commands
            .GetAsync(Arg.Any<string>(), "uptime", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CommandDto>("Command not found.", "NOT_FOUND"));
        HelpBuiltin help = HelpOver(commands, "uptime");

        Result<string> authored = await help.ExecuteAsync(Context("hug"));
        Result<string> builtin = await help.ExecuteAsync(Context("uptime"));

        authored
            .Value.Should()
            .Be("!hug — no help text yet. Use !commands to see what's available.");
        builtin
            .Value.Should()
            .Be("!uptime — no help text yet. Use !commands to see what's available.");
    }

    [Fact]
    public async Task Every_tone_of_every_help_answer_keeps_the_facts()
    {
        ICommandService commands = Substitute.For<ICommandService>();
        commands
            .GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CommandDto>("Command not found.", "NOT_FOUND"));
        HelpBuiltin help = HelpOver(commands, "uptime");

        foreach (
            string tone in new[]
            {
                PersonalityTone.Friendly,
                PersonalityTone.Sassy,
                PersonalityTone.Hype,
                PersonalityTone.Chill,
            }
        )
        {
            Result<string> usage = await help.ExecuteAsync(Context("", tone));
            Result<string> unknown = await help.ExecuteAsync(Context("zzz", tone));
            Result<string> noDescription = await help.ExecuteAsync(Context("uptime", tone));

            usage.Value.Should().Contain("!help <command>", tone);
            usage.Value.Should().Contain("!commands", tone);
            unknown.Value.Should().Contain("zzz", tone);
            unknown.Value.Should().Contain("!commands", tone);
            noDescription.Value.Should().Contain("!uptime", tone);
            noDescription.Value.Should().Contain("!commands", tone);
        }
    }

    public static TheoryData<string, string> LegacyHelpLines =>
        new()
        {
            { "accountage", "!accountage — Shows how old your Twitch account is." },
            { "banger", "!banger — Adds the currently playing song to the bangers playlist." },
            { "bansong", "!bansong [reason] — (Mod) Bans the current song and skips it." },
            { "commands", "!commands — Lists all available commands for your permission level." },
            { "discord", "!discord — Shows the Discord invite link." },
            { "followage", "!followage — Shows how long you have been following the channel." },
            { "help", "!help <command> — Shows help info for a specific command." },
            { "leaderboard", "!leaderboard — Displays the top 3 users across various categories." },
            { "lurk", "!lurk — Marks you as lurking in chat." },
            { "unlurk", "!unlurk — Marks you as no longer lurking." },
            { "playlist", "!playlist — Gives you the Spotify link to the bangers playlist." },
            { "quote", "!quote [add <text> | #number] — View or add stream quotes." },
            { "skip", "!skip — (Mod) Skips the currently playing song." },
            { "song", "!song — Shows the current song playing on stream." },
            { "sr", "!sr <spotify url or song name> — Request a song to be added to the queue." },
            { "stats", "!stats [@user] — Shows chat stats for yourself or another user." },
            { "update", "!update [@user] — Updates user info from Twitch." },
            {
                "voice",
                "!voice languages | get <lang> | set <voice> | current — Manage your TTS voice."
            },
            { "volume", "!volume [0-100] — (Mod) Gets or sets the music volume." },
            { "whisper", "!whisper <text> — Whispers your message dramatically." },
        };

    private static ICommandService NoAuthoredCommands()
    {
        ICommandService commands = Substitute.For<ICommandService>();
        commands
            .GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CommandDto>("Command not found.", "NOT_FOUND"));
        return commands;
    }

    [Theory]
    [MemberData(nameof(LegacyHelpLines))]
    public async Task Help_for_an_enabled_builtin_answers_the_old_bots_line(
        string builtinKey,
        string legacyLine
    )
    {
        HelpBuiltin help = HelpOver(NoAuthoredCommands(), builtinKey);

        Result<string> result = await help.ExecuteAsync(Context(builtinKey));

        result.Value.Should().Be(legacyLine);
    }

    [Fact]
    public async Task Help_line_uses_the_channel_prefix_not_a_fixed_bang()
    {
        HelpBuiltin help = HelpOver(NoAuthoredCommands(), "bansong");

        Result<string> result = await help.ExecuteAsync(Context("bansong", prefix: "?"));

        result.Value.Should().Be("?bansong [reason] — (Mod) Bans the current song and skips it.");
    }

    [Fact]
    public async Task Help_for_a_builtin_that_is_not_enabled_says_unknown_even_with_a_line()
    {
        HelpBuiltin help = HelpOver(NoAuthoredCommands(), "uptime");

        Result<string> result = await help.ExecuteAsync(Context("sr"));

        result.Value.Should().StartWith("Unknown command \"sr\"");
    }

    [Fact]
    public async Task Help_for_an_enabled_builtin_without_a_line_still_says_no_help_text()
    {
        HelpBuiltin help = HelpOver(NoAuthoredCommands(), "uptime");

        Result<string> result = await help.ExecuteAsync(Context("uptime"));

        result
            .Value.Should()
            .Be("!uptime — no help text yet. Use !commands to see what's available.");
    }

    [Fact]
    public async Task An_authored_command_with_a_builtins_name_keeps_answering_its_own_description()
    {
        ICommandService commands = Substitute.For<ICommandService>();
        commands
            .GetAsync(Arg.Any<string>(), "sr", Arg.Any<CancellationToken>())
            .Returns(Result.Success(FakeCommand("sr", "Channel specific request help.")));
        HelpBuiltin help = HelpOver(commands, "sr");

        Result<string> result = await help.ExecuteAsync(Context("sr"));

        result.Value.Should().Be("!sr — Channel specific request help.");
    }

    [Fact]
    public async Task Every_tone_of_a_builtin_help_line_keeps_the_legacy_line()
    {
        HelpBuiltin help = HelpOver(NoAuthoredCommands(), "sr");

        foreach (
            string tone in new[]
            {
                PersonalityTone.Friendly,
                PersonalityTone.Sassy,
                PersonalityTone.Hype,
                PersonalityTone.Chill,
            }
        )
        {
            Result<string> result = await help.ExecuteAsync(Context("sr", tone));
            result
                .Value.Should()
                .Contain(
                    "!sr <spotify url or song name> — Request a song to be added to the queue.",
                    tone
                );
        }
    }

    [Fact]
    public void Every_builtin_help_line_slot_has_its_label_and_description_keys_in_the_manifest()
    {
        string manifest = File.ReadAllText(FindManifest());
        List<string> missing = [];

        foreach (string builtinKey in LegacyHelpLines.Select(row => (string)row[0]))
        {
            string slot = BuiltinResponseSlots.Help.LineFor(builtinKey);
            string[] keys =
            [
                BuiltinReplyLabels.Label(BuiltinResponseSlots.Help.Key, slot).Key,
                BuiltinReplyLabels.Description(BuiltinResponseSlots.Help.Key, slot).Key,
            ];
            foreach (string key in keys.Select(k => k.ToLowerInvariant()))
                if (!manifest.Contains($"\"{key}\""))
                    missing.Add(key);
        }

        missing.Should().BeEmpty();
    }

    private static string FindManifest()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "server/i18n/schema-i18n-keys.manifest.json"
            );
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("schema-i18n-keys.manifest.json not found");
    }
}
