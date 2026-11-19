// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Tests.Platform.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// On a channel that never onboarded there is no owner token, so the signed-in moderator's OWN Twitch access is
/// the floor (Stoney, 2026-11-06). A dashboard request signs as the moderator who is asking, not only as a
/// moderator who happens to be reading chat; onboarding then raises it to the owner's token.
/// </summary>
public sealed class ActingModeratorResolverServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192c000-0000-7000-8000-0000000000a1");
    private static readonly Guid Owner = Guid.Parse("0192c000-0000-7000-8000-0000000000a2");
    private static readonly Guid Moderator = Guid.Parse("0192c000-0000-7000-8000-0000000000a3");

    private static PipelineOptionsTestDbContext Seed(bool onboarded, ManagementRole? moderatorRole)
    {
        PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Owner,
                Name = "kenku",
                NameNormalized = "kenku",
                TwitchChannelId = "170805317",
                IsOnboarded = onboarded,
            }
        );
        if (moderatorRole is { } role)
            db.ChannelMemberships.Add(
                new()
                {
                    BroadcasterId = Channel,
                    UserId = Moderator,
                    ManagementRole = role,
                    Source = MembershipSource.TwitchBadge,
                }
            );
        db.SaveChanges();
        return db;
    }

    private static ActingModeratorResolverService Resolver(
        PipelineOptionsTestDbContext db,
        Guid? signedInUser,
        Guid? readingModerator = null
    )
    {
        IModeratorChatReaders readers = Substitute.For<IModeratorChatReaders>();
        List<ModeratorChatReaderStatus> reading = readingModerator is { } reader
            ?
            [
                new ModeratorChatReaderStatus(
                    "170805317",
                    ModeratorChatReaderState.Reading,
                    reader,
                    null
                ),
            ]
            : [];
        readers.Snapshot().Returns(_ => reading);
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(signedInUser?.ToString());
        currentUser.IsAuthenticated.Returns(signedInUser is not null);
        return new(db, readers, currentUser);
    }

    [Fact]
    public async Task A_signed_in_moderator_signs_as_themself_on_a_channel_that_is_not_onboarded()
    {
        PipelineOptionsTestDbContext db = Seed(onboarded: false, ManagementRole.Moderator);

        Result<ActingModerator> acting = await Resolver(db, Moderator).ResolveAsync(Channel);

        acting.IsSuccess.Should().BeTrue(acting.ErrorMessage);
        acting.Value.OperatorUserId.Should().Be(Moderator);
        acting.Value.BroadcasterTwitchId.Should().Be("170805317");
    }

    [Fact]
    public async Task An_onboarded_channel_still_signs_as_its_owner_even_with_a_moderator_signed_in()
    {
        PipelineOptionsTestDbContext db = Seed(onboarded: true, ManagementRole.Moderator);

        Result<ActingModerator> acting = await Resolver(db, Moderator).ResolveAsync(Channel);

        acting.Value.OperatorUserId.Should().Be(Owner);
    }

    // Stoney, 2026-11-18: "EVERYTHING IS OPT-IN". With no person asking, the bot acts in a moderator's name
    // on a channel that did not onboard only when that streamer's yes is on record for that moderator.
    [Fact]
    public async Task An_automatic_action_on_a_channel_that_did_not_opt_in_is_refused()
    {
        PipelineOptionsTestDbContext db = Seed(onboarded: false, ManagementRole.Moderator);

        Result<ActingModerator> acting = await Resolver(db, signedInUser: null, Moderator)
            .ResolveAsync(Channel);

        acting.IsFailure.Should().BeTrue();
        acting.ErrorCode.Should().Be("NOT_OPTED_IN");
    }

    [Fact]
    public async Task An_automatic_action_signs_as_the_reading_moderator_once_the_streamer_opted_in()
    {
        PipelineOptionsTestDbContext db = Seed(onboarded: false, ManagementRole.Moderator);
        db.ModeratorMassBanOptIns.Add(
            new()
            {
                OperatorUserId = Moderator,
                BroadcasterTwitchId = "170805317",
                BroadcasterLogin = "kenku",
                Note = "asked in Discord",
                RecordedAt = DateTime.UtcNow,
            }
        );
        db.SaveChanges();

        Result<ActingModerator> acting = await Resolver(db, signedInUser: null, Moderator)
            .ResolveAsync(Channel);

        acting.IsSuccess.Should().BeTrue(acting.ErrorMessage);
        acting.Value.OperatorUserId.Should().Be(Moderator);
    }

    // Stoney, 2026-11-19: "Your own yes is enough." A moderator may ban there already, so their own switch
    // lets spam defence act in their name while they read the chat.
    [Fact]
    public async Task An_automatic_action_signs_as_the_reading_moderator_who_said_yes_themself()
    {
        PipelineOptionsTestDbContext db = Seed(onboarded: false, ManagementRole.Moderator);
        db.ChannelMemberships.Single().RemovesSpamInOwnName = true;
        db.SaveChanges();

        Result<ActingModerator> acting = await Resolver(db, signedInUser: null, Moderator)
            .ResolveAsync(Channel);

        acting.IsSuccess.Should().BeTrue(acting.ErrorMessage);
        acting.Value.OperatorUserId.Should().Be(Moderator);
        acting.Value.BroadcasterTwitchId.Should().Be("170805317");
    }

    [Fact]
    public async Task A_yes_from_a_moderator_who_is_not_reading_the_chat_signs_nothing()
    {
        PipelineOptionsTestDbContext db = Seed(onboarded: false, ManagementRole.Moderator);
        db.ChannelMemberships.Single().RemovesSpamInOwnName = true;
        db.SaveChanges();
        Guid otherReader = Guid.Parse("0192c000-0000-7000-8000-0000000000a4");

        Result<ActingModerator> acting = await Resolver(db, signedInUser: null, otherReader)
            .ResolveAsync(Channel);

        acting.IsFailure.Should().BeTrue();
        acting.ErrorCode.Should().Be("NOT_OPTED_IN");
    }

    [Fact]
    public async Task A_yes_on_a_removed_moderator_role_signs_nothing()
    {
        PipelineOptionsTestDbContext db = Seed(onboarded: false, ManagementRole.Moderator);
        ChannelMembership membership = db.ChannelMemberships.Single();
        membership.RemovesSpamInOwnName = true;
        membership.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();

        Result<ActingModerator> acting = await Resolver(db, signedInUser: null, Moderator)
            .ResolveAsync(Channel);

        acting.IsFailure.Should().BeTrue();
        acting.ErrorCode.Should().Be("NOT_OPTED_IN");
    }

    [Fact]
    public async Task A_signed_in_user_without_a_management_role_is_not_used()
    {
        PipelineOptionsTestDbContext db = Seed(onboarded: false, moderatorRole: null);

        Result<ActingModerator> acting = await Resolver(db, Moderator).ResolveAsync(Channel);

        acting.IsFailure.Should().BeTrue();
        acting.ErrorCode.Should().Be("NO_ACTING_IDENTITY");
    }
}
