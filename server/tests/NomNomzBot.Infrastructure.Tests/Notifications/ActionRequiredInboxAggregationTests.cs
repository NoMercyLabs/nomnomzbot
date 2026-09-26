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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Notifications;
using NomNomzBot.Infrastructure.Notifications.Sources;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// Proves what the inbox itself owns on top of its sources: one failing source never blanks the others, a
/// dismiss is routed to the source that minted the id, and an id no source owns is refused.
/// </summary>
public sealed class ActionRequiredInboxAggregationTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192b000-0000-7000-8000-0000000000f1");

    [Fact]
    public async Task GetItemsAsync_KeepsTheOtherSourcesItems_WhenOneSourceFails()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        db.IntegrationConnections.Add(
            new IntegrationConnection
            {
                BroadcasterId = ChannelId,
                Provider = AuthEnums.IntegrationProvider.Spotify,
                Status = AuthEnums.IntegrationStatus.Expired,
                LastErrorAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
            }
        );
        await db.SaveChangesAsync();
        ActionRequiredInboxService sut = new(
            [new FailingSource(), new DeadIntegrationTokenSource(db)],
            db,
            TimeProvider.System,
            NullLogger<ActionRequiredInboxService>.Instance
        );

        Result<List<ActionRequiredItemDto>> result = await sut.GetItemsAsync(ChannelId);

        result.IsSuccess.Should().BeTrue();
        ActionRequiredItemDto item = result.Value.Should().ContainSingle().Subject;
        item.Kind.Should().Be("integration_token_dead");
        item.MessageKey.Should().Be("attention_integration_expired_message");
    }

    [Fact]
    public async Task DismissAsync_RefusesAnIdNoSourceOwns_AndWritesNothing()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        ActionRequiredInboxService sut = ActionRequiredInboxHarness.Create(db);

        Result<int> result = await sut.DismissAsync(ChannelId, Guid.NewGuid(), ["bogus:123"]);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        db.ActionRequiredDismissals.Should().BeEmpty();
    }

    private sealed class FailingSource : IActionRequiredSource
    {
        public IReadOnlyCollection<string> KeyPrefixes { get; } = ["failing:"];

        public IReadOnlyCollection<string> InvalidatingEventTypes { get; } = [];

        public Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
            Guid channelId,
            IReadOnlySet<string> dismissedKeys,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                Result.Failure<List<ActionRequiredItemDto>>("source is down", "INTERNAL_ERROR")
            );

        public Task<Result<List<string>>> ResolveDismissalKeysAsync(
            Guid channelId,
            string itemId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success<List<string>>([itemId]));
    }
}
