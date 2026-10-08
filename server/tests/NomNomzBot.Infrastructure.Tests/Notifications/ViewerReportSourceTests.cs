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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Infrastructure.Notifications.Sources;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// Open viewer reports reach the moderators' attention inbox: one item per open report, scoped to its channel,
/// leaving the inbox once a moderator resolves the report or the item is dismissed, and refreshing the inbox live
/// when a report is filed or resolved.
/// </summary>
public sealed class ViewerReportSourceTests
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-0000000000d1");
    private static readonly Guid OtherChannel = Guid.Parse("0192b000-0000-7000-8000-0000000000d2");

    private static async Task<(
        ActionRequiredInboxServiceTestDbContext Db,
        ViewerReport First,
        ViewerReport Second
    )> SeedAsync()
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        User griefer = new()
        {
            Id = Guid.CreateVersion7(),
            Username = "griefer",
            UsernameNormalized = "griefer",
            DisplayName = "Griefer",
        };
        db.Users.Add(griefer);

        ViewerReport first = NewReport(Channel, griefer.Id, "5005", "spamming links");
        ViewerReport second = NewReport(Channel, griefer.Id, "5005", "hate speech");
        db.ViewerReports.AddRange(first, second, NewReport(OtherChannel, griefer.Id, "5005", "x"));
        await db.SaveChangesAsync();
        return (db, first, second);
    }

    private static ViewerReport NewReport(
        Guid channel,
        Guid reportedUser,
        string twitchId,
        string reason
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = channel,
            ReportedUserId = reportedUser,
            ReportedTwitchUserId = twitchId,
            Reason = reason,
            Status = "open",
        };

    [Fact]
    public async Task Each_open_report_becomes_one_warning_item_naming_the_reported_chatter_and_reason()
    {
        (ActionRequiredInboxServiceTestDbContext db, ViewerReport first, ViewerReport second) =
            await SeedAsync();

        Result<List<ActionRequiredItemDto>> items = await new ViewerReportSource(db).GetItemsAsync(
            Channel,
            new HashSet<string>()
        );

        items.IsSuccess.Should().BeTrue(items.ErrorMessage);
        items.Value.Should().HaveCount(2, "the other channel's report never shows here");
        ActionRequiredItemDto item = items.Value.Single(i => i.Id == $"report:{first.Id}");
        item.Kind.Should().Be("viewer_report");
        item.Severity.Should().Be("warning");
        item.TitleKey.Should().Be("attention_report_title");
        item.MessageKey.Should().Be("attention_report_message");
        item.Parameters.Should()
            .BeEquivalentTo(
                new Dictionary<string, string>
                {
                    ["username"] = "Griefer",
                    ["reason"] = "spamming links",
                }
            );
        item.DeepLinkRoute.Should().Be("moderationqueue");
        item.SourceUserId.Should().Be("5005");
        item.Count.Should().Be(1);
        items.Value.Select(i => i.Id).Should().Contain($"report:{second.Id}");
    }

    [Fact]
    public async Task A_resolved_report_leaves_the_inbox_while_the_other_stays()
    {
        (ActionRequiredInboxServiceTestDbContext db, ViewerReport first, ViewerReport second) =
            await SeedAsync();
        first.Status = "dismissed";
        await db.SaveChangesAsync();

        Result<List<ActionRequiredItemDto>> items = await new ViewerReportSource(db).GetItemsAsync(
            Channel,
            new HashSet<string>()
        );

        items.Value.Select(i => i.Id).Should().Equal($"report:{second.Id}");
    }

    [Fact]
    public async Task A_dismissed_inbox_item_stays_hidden_and_resolves_to_its_own_key()
    {
        (ActionRequiredInboxServiceTestDbContext db, ViewerReport first, ViewerReport second) =
            await SeedAsync();
        ViewerReportSource source = new(db);

        Result<List<ActionRequiredItemDto>> items = await source.GetItemsAsync(
            Channel,
            new HashSet<string> { $"report:{first.Id}" }
        );
        Result<List<string>> keys = await source.ResolveDismissalKeysAsync(
            Channel,
            $"report:{first.Id}"
        );

        items.Value.Select(i => i.Id).Should().Equal($"report:{second.Id}");
        keys.Value.Should().Equal($"report:{first.Id}");
    }

    [Fact]
    public async Task Filing_or_resolving_a_report_refreshes_the_channels_inbox_live()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        RecordingChangeNotifier notifier = new();
        Infrastructure.Notifications.ActionRequiredInvalidationHook hook = new(
            ActionRequiredInboxHarness.Sources(db, TimeProvider.System),
            notifier
        );

        await hook.OnCommittedAsync(
            Record(nameof(NomNomzBot.Domain.Platform.Events.ChannelConfigChangedEvent))
        );

        notifier.Signalled.Should().Equal(Channel);
    }

    private static EventRecord Record(string eventType) =>
        new(
            Id: 1,
            EventId: Guid.NewGuid(),
            BroadcasterId: Channel,
            StreamPosition: 1,
            EventType: eventType,
            EventVersion: 1,
            Source: "domain",
            PayloadJson: "{}",
            PayloadIsEncrypted: false,
            SubjectKeyId: null,
            CorrelationId: null,
            CausationId: null,
            ActorUserId: null,
            ActorExternalUserId: null,
            ActorProvider: null,
            MetadataJson: "{}",
            OccurredAt: DateTime.UtcNow,
            RecordedAt: DateTime.UtcNow
        );

    [Fact]
    public void The_source_owns_only_the_report_key_prefix()
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();

        IActionRequiredSource source = new ViewerReportSource(db);

        source.KeyPrefixes.Should().Equal("report:");
        source.InvalidatingEventTypes.Should().Contain("ChannelConfigChangedEvent");
    }
}
