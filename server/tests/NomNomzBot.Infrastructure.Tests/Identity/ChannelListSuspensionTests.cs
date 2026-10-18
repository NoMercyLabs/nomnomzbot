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
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Platform.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Configuration;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// A suspended channel stays in its people's channel list with the reason, the date and the support contact, so
/// the dashboard shows the suspended view; an active channel carries no suspension.
/// </summary>
public sealed class ChannelListSuspensionTests
{
    private const string ContactUrl = "https://support.example.org/contact";

    [Fact]
    public async Task A_suspended_channel_lists_with_its_reason_date_and_the_admin_set_contact()
    {
        AuthDbContext db = AuthTestBuilder.NewContext(Guid.NewGuid().ToString());
        SupportContactService contact = new(db, new ConfigurationBuilder().Build());
        (await contact.SetContactUrlAsync(ContactUrl)).IsSuccess.Should().BeTrue();
        (Guid ownerId, Guid channelId) = await SeedChannelAsync(db, contact);

        DateTime suspendedAt = new(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
        Channel channel = await db.Channels.SingleAsync(c => c.Id == channelId);
        channel.Status = AuthEnums.ChannelStatus.Suspended;
        channel.SuspendedReason = "Spam reports";
        channel.SuspendedAt = suspendedAt;
        await db.SaveChangesAsync();

        ChannelSummaryDto row = await OnlyRowAsync(db, contact, ownerId);

        row.Suspension.Should()
            .Be(
                new ChannelSuspensionDto(
                    AuthEnums.ChannelStatus.Suspended,
                    "Spam reports",
                    suspendedAt,
                    ContactUrl
                )
            );
    }

    [Fact]
    public async Task An_active_channel_lists_without_a_suspension()
    {
        AuthDbContext db = AuthTestBuilder.NewContext(Guid.NewGuid().ToString());
        SupportContactService contact = new(db, new ConfigurationBuilder().Build());
        (Guid ownerId, Guid _) = await SeedChannelAsync(db, contact);

        ChannelSummaryDto row = await OnlyRowAsync(db, contact, ownerId);

        row.Suspension.Should().BeNull();
    }

    [Fact]
    public async Task The_support_contact_falls_back_to_the_setting_and_refuses_a_non_web_address()
    {
        AuthDbContext db = AuthTestBuilder.NewContext(Guid.NewGuid().ToString());
        IConfiguration settings = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [SupportContactService.FallbackSetting] = "https://fallback.example.org",
                }
            )
            .Build();
        SupportContactService contact = new(db, settings);

        (await contact.GetContactUrlAsync()).Should().Be("https://fallback.example.org");

        Result<string?> refused = await contact.SetContactUrlAsync("javascript:alert(1)");
        refused.IsFailure.Should().BeTrue();
        (await db.Configurations.CountAsync()).Should().Be(0);

        Result<string?> saved = await contact.SetContactUrlAsync(ContactUrl);
        saved.Value.Should().Be(ContactUrl);
        (await db.Configurations.SingleAsync()).Value.Should().Be(ContactUrl);
    }

    private static async Task<(Guid OwnerId, Guid ChannelId)> SeedChannelAsync(
        AuthDbContext db,
        ISupportContactService contact
    )
    {
        User owner = new()
        {
            TwitchUserId = "tw-300",
            Username = "streamer",
            UsernameNormalized = "streamer",
            DisplayName = "Streamer",
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        Result<Guid> tenant = await Build(db, contact)
            .EnsureModeratedTenantAsync("tw-300", "streamer", "Streamer", owner.Id);
        tenant.IsSuccess.Should().BeTrue();
        return (owner.Id, tenant.Value);
    }

    private static async Task<ChannelSummaryDto> OnlyRowAsync(
        AuthDbContext db,
        ISupportContactService contact,
        Guid ownerId
    )
    {
        Result<PagedList<ChannelSummaryDto>> list = await Build(db, contact)
            .GetChannelsAsync(ownerId.ToString(), new(1, 50, null, null));
        list.IsSuccess.Should().BeTrue();
        return list.Value.Items.Should().ContainSingle().Subject;
    }

    private static ChannelService Build(AuthDbContext db, ISupportContactService contact) =>
        new(
            db,
            TimeProvider.System,
            new RecordingEventBus(),
            Substitute.For<IChannelRegistry>(),
            Substitute.For<ITwitchEventSubService>(),
            Substitute.For<IChatProvider>(),
            Substitute.For<IBuiltinResponseComposer>(),
            Substitute.For<IBotModeratorStatusService>(),
            contact
        );
}
