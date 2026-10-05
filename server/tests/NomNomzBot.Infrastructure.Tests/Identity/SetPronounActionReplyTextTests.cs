// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Identity.PipelineActions;
using NSubstitute;
using ActionDefinition = NomNomzBot.Application.Abstractions.Pipeline.ActionDefinition;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// The <c>set_pronoun</c> action returns the legacy <c>!setpronoun</c> reply texts (legacy SetPronoun.cs:30-90),
/// so a <c>send_reply</c> step that shows <c>{last.output}</c> / <c>{last.error}</c> reads the same as the old bot.
/// </summary>
public sealed class SetPronounActionReplyTextTests
{
    private static readonly Guid TargetId = Guid.CreateVersion7();

    private static PipelineExecutionContext Context() =>
        new()
        {
            BroadcasterId = Guid.CreateVersion7(),
            TriggeredByUserId = Guid.CreateVersion7().ToString(),
            TriggeredByDisplayName = "mod",
            MessageId = "m1",
            RawMessage = "!setpronoun",
            CancellationToken = default,
        };

    private static ActionDefinition Action(string username, string pronoun) =>
        new()
        {
            Type = "set_pronoun",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["username"] = JsonSerializer.SerializeToElement(username),
                ["pronoun"] = JsonSerializer.SerializeToElement(pronoun),
            },
        };

    private static async Task<(ActionResult Result, IPronounSelfService Pronouns)> RunAsync(
        string username,
        string pronoun,
        bool userFound = true
    )
    {
        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();
        IReadOnlyList<TwitchUser> found = userFound
            ? [new("42", "bob", "Bob", "", "", "", "", "", 0, DateTimeOffset.UnixEpoch)]
            : [];
        twitch
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(found));

        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync("42", "bob", "Bob", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new UserDto(
                        TargetId.ToString(),
                        "bob",
                        "Bob",
                        null,
                        null,
                        DateTime.UnixEpoch,
                        DateTime.UnixEpoch
                    )
                )
            );

        IPronounSelfService pronouns = Substitute.For<IPronounSelfService>();
        pronouns
            .SetAsync(Arg.Any<Guid>(), Arg.Any<SetPronounRequest>(), Arg.Any<CancellationToken>())
            .Returns(new UserPronounDto(1, "he/him", "he/him", null, null, true));

        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Pronouns.AddRange(
            new Pronoun
            {
                Name = "he/him",
                Subject = "he",
                Object = "him",
                Possessive = "his",
                GenderedTerm = "guy",
            },
            new Pronoun
            {
                Name = "they/them",
                Subject = "they",
                Object = "them",
                Possessive = "their",
                GenderedTerm = "person",
            }
        );
        await db.SaveChangesAsync();

        ActionResult result = await new SetPronounAction(twitch, users, pronouns, db).ExecuteAsync(
            Context(),
            Action(username, pronoun)
        );
        return (result, pronouns);
    }

    [Fact]
    public async Task Missing_arguments_return_the_legacy_usage_line()
    {
        (ActionResult result, _) = await RunAsync("", "");

        result.Succeeded.Should().BeFalse();
        result
            .ErrorMessage.Should()
            .Be(
                "Usage: !setpronoun <username> <pronoun> — e.g. !setpronoun someone he/him, she/her, they/them, or clear"
            );
    }

    [Fact]
    public async Task Clear_returns_the_legacy_text_and_resets_the_override()
    {
        (ActionResult result, IPronounSelfService pronouns) = await RunAsync("bob", "clear");

        result.Succeeded.Should().BeTrue();
        result
            .Output.Should()
            .Be("Cleared pronoun override for Bob. Will use their alejo.io setting next time.");
        await pronouns
            .Received(1)
            .SetAsync(
                TargetId,
                Arg.Is<SetPronounRequest>(r => r.ManualOverride == false && r.PronounId == 0),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Set_returns_the_legacy_text_with_name_subject_and_object()
    {
        (ActionResult result, IPronounSelfService pronouns) = await RunAsync("bob", "he/him");

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Be("Set pronouns for Bob to he/him (he/him).");
        await pronouns
            .Received(1)
            .SetAsync(
                TargetId,
                Arg.Is<SetPronounRequest>(r => r.ManualOverride == true && r.PronounId > 0),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Unknown_pronoun_returns_the_legacy_text_with_the_catalogue()
    {
        (ActionResult result, IPronounSelfService pronouns) = await RunAsync("bob", "xe/xem");

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("Unknown pronoun 'xe/xem'. Available: he/him, they/them");
        await pronouns
            .DidNotReceive()
            .SetAsync(Arg.Any<Guid>(), Arg.Any<SetPronounRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_user_returns_the_legacy_could_not_find_text()
    {
        (ActionResult result, _) = await RunAsync("ghost", "he/him", userFound: false);

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("Could not find user 'ghost'.");
    }
}
