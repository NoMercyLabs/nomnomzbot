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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Identity.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity.PipelineActions;

/// <summary>
/// Proves <c>set_pronoun</c> (the mod-only <c>!setpronoun</c> flow) resolves the TARGET's Twitch login to a
/// platform user and calls the pronoun self-service on THEIR behalf with ManualOverride pinned — the behavior
/// that distinguishes it from the caller-only self-service surface — and that "clear" resets the override.
/// </summary>
public sealed class SetPronounActionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000c301");
    private static readonly Guid TargetUserId = Guid.Parse("0192a000-0000-7000-8000-00000000c3a2");

    private static PipelineExecutionContext Ctx() =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "mod-1",
            TriggeredByDisplayName = "Mod",
            MessageId = "m1",
            RawMessage = "!setpronoun coolstreamer they/them",
        };

    private static TwitchUser User(string id, string login) =>
        new(
            Id: id,
            Login: login,
            DisplayName: login,
            Type: "",
            BroadcasterType: "",
            Description: "",
            ProfileImageUrl: "",
            OfflineImageUrl: "",
            ViewCount: 0,
            CreatedAt: DateTimeOffset.UnixEpoch
        );

    private static (SetPronounAction Sut, IPronounSelfService Pronouns) Build(AuthDbContext db)
    {
        ITwitchUsersApi twitchUsers = Substitute.For<ITwitchUsersApi>();
        twitchUsers
            .GetUsersByLoginsAsync(
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == "coolstreamer"),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([User("789", "coolstreamer")]));

        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                "789",
                "coolstreamer",
                "coolstreamer",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new UserDto(
                        TargetUserId.ToString(),
                        "coolstreamer",
                        "coolstreamer",
                        null,
                        null,
                        DateTime.UnixEpoch,
                        DateTime.UnixEpoch
                    )
                )
            );

        IPronounSelfService pronouns = Substitute.For<IPronounSelfService>();
        pronouns
            .SetAsync(TargetUserId, Arg.Any<SetPronounRequest>(), Arg.Any<CancellationToken>())
            .Returns(new UserPronounDto(null, null, null, null, null, false));

        db.Pronouns.Add(
            new()
            {
                Id = 3,
                Name = "they/them",
                Subject = "they",
                Object = "them",
                Possessive = "their",
                GenderedTerm = "they",
            }
        );
        db.SaveChanges();

        SetPronounAction sut = new(twitchUsers, users, pronouns, db);
        return (sut, pronouns);
    }

    [Fact]
    public async Task Resolves_the_targets_login_and_pins_the_override()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        (SetPronounAction sut, IPronounSelfService pronouns) = Build(db);

        ActionResult result = await sut.ExecuteAsync(
            Ctx(),
            new()
            {
                Type = "set_pronoun",
                Parameters = new()
                {
                    ["username"] = System.Text.Json.JsonSerializer.SerializeToElement(
                        "@CoolStreamer"
                    ),
                    ["pronoun"] = System.Text.Json.JsonSerializer.SerializeToElement("they/them"),
                },
            }
        );

        result.Succeeded.Should().BeTrue();
        await pronouns
            .Received(1)
            .SetAsync(
                TargetUserId,
                Arg.Is<SetPronounRequest>(r => r.PronounId == 3 && r.ManualOverride == true),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Clear_resets_the_override_instead_of_looking_up_a_pronoun()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        (SetPronounAction sut, IPronounSelfService pronouns) = Build(db);

        ActionResult result = await sut.ExecuteAsync(
            Ctx(),
            new()
            {
                Type = "set_pronoun",
                Parameters = new()
                {
                    ["username"] = System.Text.Json.JsonSerializer.SerializeToElement(
                        "coolstreamer"
                    ),
                    ["pronoun"] = System.Text.Json.JsonSerializer.SerializeToElement("clear"),
                },
            }
        );

        result.Succeeded.Should().BeTrue();
        await pronouns
            .Received(1)
            .SetAsync(
                TargetUserId,
                Arg.Is<SetPronounRequest>(r => r.ManualOverride == false),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_unknown_pronoun_fails_without_calling_the_service()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        (SetPronounAction sut, IPronounSelfService pronouns) = Build(db);

        ActionResult result = await sut.ExecuteAsync(
            Ctx(),
            new()
            {
                Type = "set_pronoun",
                Parameters = new()
                {
                    ["username"] = System.Text.Json.JsonSerializer.SerializeToElement(
                        "coolstreamer"
                    ),
                    ["pronoun"] = System.Text.Json.JsonSerializer.SerializeToElement("xe/xem"),
                },
            }
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("xe/xem");
        await pronouns.DidNotReceiveWithAnyArgs().SetAsync(default, default!, default);
    }
}
