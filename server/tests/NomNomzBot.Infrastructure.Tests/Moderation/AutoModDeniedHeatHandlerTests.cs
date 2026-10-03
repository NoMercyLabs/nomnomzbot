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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Application.Trust.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Trust;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The AutoMod heat leg (+5 on the default policy) fires when a moderator DENIES a held message — Twitch's
/// <c>automod.message.update</c> with status <c>denied</c> — and not when the message was approved or expired.
/// </summary>
public sealed class AutoModDeniedHeatHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000ef01");
    private static readonly Guid Subject = Guid.Parse("0192a000-0000-7000-8000-00000000ef02");
    private const string SubjectTwitchId = "viewer-77";
    private static readonly DateTime T0 = new(2026, 10, 3, 6, 0, 0, DateTimeKind.Utc);

    private static (AutoModDeniedProjectionHandler Sut, ModerationServiceTestDbContext Db) Build()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        IModerationService moderation = Substitute.For<IModerationService>();
        moderation
            .GetAutomodConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new AutomodConfigDto(
                        new(false, []),
                        new(false, 0),
                        new(false, []),
                        new(false, 0)
                    )
                )
            );
        ITrustPolicyService trustPolicy = Substitute.For<ITrustPolicyService>();
        trustPolicy
            .GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(TrustScoreCalculator.DefaultPolicy);
        RecordingEventBus bus = new();
        ModerationProjectionService projections = new(
            db,
            moderation,
            trustPolicy,
            bus,
            new FakeTimeProvider(new(T0)),
            NullLogger<ModerationProjectionService>.Instance,
            new UserIdentityService(
                db,
                Substitute.For<IServiceScopeFactory>(),
                TimeProvider.System,
                bus
            )
        );
        db.Users.Add(
            new()
            {
                Id = Subject,
                TwitchUserId = SubjectTwitchId,
                Username = "viewer77",
                UsernameNormalized = "viewer77",
                DisplayName = "Viewer77",
                CreatedAt = T0.AddYears(-2),
            }
        );
        db.UserIdentities.Add(
            new()
            {
                UserId = Subject,
                Provider = AuthEnums.Platform.Twitch,
                ProviderUserId = SubjectTwitchId,
                ProviderUsername = "viewer77",
            }
        );
        db.SaveChanges();
        return (new(projections), db);
    }

    private static AutoModMessageUpdatedEvent Update(string status) =>
        new()
        {
            BroadcasterId = Channel,
            OccurredAt = new(T0),
            MessageId = "msg-1",
            UserId = SubjectTwitchId,
            UserDisplayName = "Viewer77",
            UserLogin = "viewer77",
            ModeratorId = "mod-1",
            ModeratorDisplayName = "Mod",
            Status = status,
        };

    [Fact]
    public async Task A_denied_held_message_adds_the_automod_denied_heat()
    {
        (AutoModDeniedProjectionHandler sut, ModerationServiceTestDbContext db) = Build();

        await sut.HandleAsync(Update("denied"));

        UserTrustScore score = await db.UserTrustScores.SingleAsync();
        score.SubjectUserId.Should().Be(Subject);
        score.HeatScore.Should().Be(5m, "automod_denied is +5 heat on the default policy");
        (await db.UserModerationHistories.SingleAsync())
            .LastActionType.Should()
            .Be("automod_denied");
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("expired")]
    public async Task An_approved_or_expired_held_message_adds_no_heat(string status)
    {
        (AutoModDeniedProjectionHandler sut, ModerationServiceTestDbContext db) = Build();

        await sut.HandleAsync(Update(status));

        (await db.UserTrustScores.CountAsync()).Should().Be(0);
        (await db.UserModerationHistories.CountAsync()).Should().Be(0);
    }
}
