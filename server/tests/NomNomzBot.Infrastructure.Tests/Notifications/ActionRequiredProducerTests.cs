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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Integrations.Events;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Notifications;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// Proves each action-required producer (plan item A0) from real seeded state: the condition present yields an
/// item with the right key, severity, resource keys, parameters and deep link; the condition absent yields
/// nothing; a dismissal hides it and a NEW occurrence surfaces again; another channel's state never leaks in.
/// </summary>
public sealed class ActionRequiredProducerTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192b000-0000-7000-8000-0000000000a1");
    private static readonly Guid OtherChannelId = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000a2"
    );
    private static readonly DateTime T0 = new(2026, 9, 20, 18, 0, 0, DateTimeKind.Utc);

    // ─── EventSub refusals / missing scopes ──────────────────────────────────

    [Fact]
    public async Task ARefusedEventSubTopic_SurfacesItsScopeFeatureAndTopic_AsCritical()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await NewDbAsync();
        db.EventSubSubscriptions.Add(
            Refused(ChannelId, "channel.hype_train.begin", "Missing required scope bits:read")
        );
        db.EventSubSubscriptions.Add(
            Refused(OtherChannelId, "channel.cheer", "Missing required scope bits:read")
        );
        await db.SaveChangesAsync();

        ActionRequiredItemDto item = (await ItemsAsync(db)).Should().ContainSingle().Subject;

        item.Kind.Should().Be("twitch_scope_missing");
        item.Severity.Should().Be("critical");
        item.Id.Should().Be("twitch-scope:bits:read:0");
        item.TitleKey.Should().Be("attention_scope_missing_title");
        item.MessageKey.Should().Be("attention_scope_missing_topics_message");
        item.Parameters.Should()
            .Contain("scope", "bits:read")
            .And.Contain("feature", "bits")
            .And.Contain("topics", "channel.hype_train.begin");
        item.DeepLinkRoute.Should().Be("integrations");
    }

    [Fact]
    public async Task AnEnabledOrUnrelatedFailedSubscription_SurfacesNothing()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await NewDbAsync();
        EventSubSubscription healthy = Refused(ChannelId, "channel.follow", null);
        healthy.Status = "enabled";
        db.EventSubSubscriptions.Add(healthy);
        db.EventSubSubscriptions.Add(Refused(ChannelId, "channel.raid", "Bad Request"));
        await db.SaveChangesAsync();

        (await ItemsAsync(db)).Should().BeEmpty();
    }

    [Fact]
    public async Task ARecordedHelixScopeGap_MergesWithTheRefusedTopic_AndReSurfacesWhenRecordedAgain()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await NewDbAsync();
        db.EventSubSubscriptions.Add(
            Refused(ChannelId, "channel.follow", "Missing required scope moderator:read:followers")
        );
        ChannelMissingScope gap = new()
        {
            BroadcasterId = ChannelId,
            Scope = "moderator:read:followers",
            Feature = "followers",
            DetectedAt = T0,
        };
        db.ChannelMissingScopes.Add(gap);
        await db.SaveChangesAsync();
        ActionRequiredInboxService sut = ActionRequiredInboxHarness.Create(db);

        ActionRequiredItemDto item = (await sut.GetItemsAsync(ChannelId))
            .Value.Should()
            .ContainSingle("the refused topic and the recorded gap are ONE missing scope")
            .Subject;
        item.Id.Should().Be($"twitch-scope:moderator:read:followers:{T0.Ticks}");
        item.DetectedAt.Should().Be(T0);

        (await sut.DismissAsync(ChannelId, Guid.NewGuid(), [item.Id])).Value.Should().Be(1);
        (await sut.GetItemsAsync(ChannelId)).Value.Should().BeEmpty();

        // A re-grant removed the gap; losing the scope again records a fresh row.
        db.ChannelMissingScopes.Remove(gap);
        db.ChannelMissingScopes.Add(
            new ChannelMissingScope
            {
                BroadcasterId = ChannelId,
                Scope = "moderator:read:followers",
                Feature = "followers",
                DetectedAt = T0.AddDays(2),
            }
        );
        await db.SaveChangesAsync();

        (await sut.GetItemsAsync(ChannelId))
            .Value.Should()
            .ContainSingle(i =>
                i.Id == $"twitch-scope:moderator:read:followers:{T0.AddDays(2).Ticks}"
            );
    }

    [Fact]
    public async Task UnnamedAuthorizationRefusals_CollapseIntoOneItem_ThatReSurfacesAfterANewRefusal()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await NewDbAsync();
        EventSubSubscription follow = Refused(
            ChannelId,
            "channel.follow",
            "subscription missing proper authorization [grants:a,b]"
        );
        db.EventSubSubscriptions.Add(follow);
        db.EventSubSubscriptions.Add(
            Refused(
                ChannelId,
                "channel.ban",
                "subscription missing proper authorization [grants:a,b]"
            )
        );
        await db.SaveChangesAsync();
        ActionRequiredInboxService sut = ActionRequiredInboxHarness.Create(db);

        ActionRequiredItemDto item = (await sut.GetItemsAsync(ChannelId))
            .Value.Should()
            .ContainSingle()
            .Subject;
        item.Kind.Should().Be("eventsub_unauthorized");
        item.Severity.Should().Be("critical");
        item.MessageKey.Should().Be("attention_eventsub_unauthorized_message");
        item.Parameters.Should()
            .Contain("topics", "channel.ban, channel.follow")
            .And.Contain("count", "2");
        item.DeepLinkRoute.Should().Be("integrations");

        await sut.DismissAsync(ChannelId, Guid.NewGuid(), [item.Id]);
        (await sut.GetItemsAsync(ChannelId)).Value.Should().BeEmpty();

        // A re-grant that still does not cover the topic stores a new grant fingerprint.
        follow.LastError = "subscription missing proper authorization [grants:a,b,c]";
        await db.SaveChangesAsync();

        (await sut.GetItemsAsync(ChannelId)).Value.Should().ContainSingle(i => i.Id != item.Id);
    }

    // ─── Widget build failures ───────────────────────────────────────────────

    [Fact]
    public async Task AFailedNewestBuild_WithAnOlderBuildLive_IsAWarningNamingTheWidgetAndVersion()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await NewDbAsync();
        Widget widget = NewWidget(ChannelId, "Follower goal");
        WidgetVersion live = NewVersion(widget, 1, "success");
        WidgetVersion failed = NewVersion(widget, 2, "error");
        widget.ActiveVersionId = live.Id;
        db.Widgets.Add(widget);
        db.WidgetVersions.AddRange(live, failed);
        await db.SaveChangesAsync();

        ActionRequiredItemDto item = (await ItemsAsync(db)).Should().ContainSingle().Subject;

        item.Id.Should().Be($"widget-build:{failed.Id}");
        item.Kind.Should().Be("widget_build_failed");
        item.Severity.Should().Be("warning");
        item.MessageKey.Should().Be("attention_widget_build_failed_message");
        item.Parameters.Should().Contain("widgetName", "Follower goal").And.Contain("version", "2");
        item.DeepLinkRoute.Should().Be("widgets");
    }

    [Fact]
    public async Task AFailedBuild_WithNothingLive_IsCritical()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await NewDbAsync();
        Widget widget = NewWidget(ChannelId, "Alert box");
        db.Widgets.Add(widget);
        db.WidgetVersions.Add(NewVersion(widget, 1, "error"));
        await db.SaveChangesAsync();

        ActionRequiredItemDto item = (await ItemsAsync(db)).Should().ContainSingle().Subject;

        item.Severity.Should().Be("critical");
        item.MessageKey.Should().Be("attention_widget_build_failed_nothing_live_message");
    }

    [Fact]
    public async Task AFixedBuild_OrADisabledWidget_SurfacesNothing()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await NewDbAsync();
        Widget fixedWidget = NewWidget(ChannelId, "Fixed");
        db.Widgets.Add(fixedWidget);
        db.WidgetVersions.AddRange(
            NewVersion(fixedWidget, 1, "error"),
            NewVersion(fixedWidget, 2, "success")
        );
        Widget disabled = NewWidget(ChannelId, "Parked");
        disabled.IsEnabled = false;
        db.Widgets.Add(disabled);
        db.WidgetVersions.Add(NewVersion(disabled, 1, "error"));
        await db.SaveChangesAsync();

        (await ItemsAsync(db)).Should().BeEmpty();
    }

    // ─── Outbound webhooks ───────────────────────────────────────────────────

    [Fact]
    public async Task AnAutoDisabledEndpoint_IsCritical_AndANewDisableSurfacesAgainAfterDismissal()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await NewDbAsync();
        OutboundWebhookEndpoint endpoint = NewEndpoint(ChannelId, "Discord relay");
        endpoint.IsEnabled = false;
        endpoint.ConsecutiveFailureCount = 20;
        endpoint.DisabledAt = T0;
        db.OutboundWebhookEndpoints.Add(endpoint);
        await db.SaveChangesAsync();
        ActionRequiredInboxService sut = ActionRequiredInboxHarness.Create(db);

        ActionRequiredItemDto item = (await sut.GetItemsAsync(ChannelId))
            .Value.Should()
            .ContainSingle()
            .Subject;
        item.Id.Should().Be($"webhook-disabled:{endpoint.Id}:{T0.Ticks}");
        item.Kind.Should().Be("webhook_endpoint_disabled");
        item.Severity.Should().Be("critical");
        item.Parameters.Should()
            .Contain("endpointName", "Discord relay")
            .And.Contain("failureCount", "20");
        item.DeepLinkRoute.Should().Be("webhooks");

        await sut.DismissAsync(ChannelId, Guid.NewGuid(), [item.Id]);
        (await sut.GetItemsAsync(ChannelId)).Value.Should().BeEmpty();

        endpoint.DisabledAt = T0.AddDays(1);
        await db.SaveChangesAsync();
        (await sut.GetItemsAsync(ChannelId)).Value.Should().ContainSingle();
    }

    [Fact]
    public async Task AFailureStreak_IsAWarningOnlyFromTheThreshold()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await NewDbAsync();
        OutboundWebhookEndpoint flaky = NewEndpoint(ChannelId, "Flaky");
        flaky.ConsecutiveFailureCount = 2;
        OutboundWebhookEndpoint down = NewEndpoint(ChannelId, "Down");
        down.ConsecutiveFailureCount = 3;
        down.LastSuccessAt = T0;
        db.OutboundWebhookEndpoints.AddRange(flaky, down);
        await db.SaveChangesAsync();

        ActionRequiredItemDto item = (await ItemsAsync(db)).Should().ContainSingle().Subject;

        item.Id.Should().Be($"webhook-failing:{down.Id}:{T0.Ticks}");
        item.Kind.Should().Be("webhook_deliveries_failing");
        item.Severity.Should().Be("warning");
        item.Parameters.Should().Contain("endpointName", "Down");
    }

    // ─── Song requests lost at the provider ──────────────────────────────────

    [Fact]
    public async Task ALostSongRequest_InTheWindow_NamesTheTrackAndRequester()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await NewDbAsync();
        FakeTimeProvider clock = new(T0.AddHours(2));
        db.EventJournals.AddRange(
            LostEvent(ChannelId, 1, T0, "Family Ties", "viewer_one"),
            LostEvent(ChannelId, 2, T0.AddHours(-30), "Too Old", "viewer_two"),
            LostEvent(OtherChannelId, 1, T0, "Not Mine", "viewer_three")
        );
        await db.SaveChangesAsync();

        ActionRequiredItemDto item = (
            await ActionRequiredInboxHarness.Create(db, clock).GetItemsAsync(ChannelId)
        )
            .Value.Should()
            .ContainSingle()
            .Subject;

        item.Kind.Should().Be("song_request_lost");
        item.Severity.Should().Be("info");
        item.Count.Should().Be(1);
        item.MessageKey.Should().Be("attention_song_lost_message");
        item.Parameters.Should()
            .Contain("trackName", "Family Ties")
            .And.Contain("requestedBy", "viewer_one");
        item.DeepLinkRoute.Should().Be("songrequests");
    }

    [Fact]
    public async Task ANewLostSongRequest_SurfacesAgainAfterDismissingTheOldOnes()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await NewDbAsync();
        FakeTimeProvider clock = new(T0.AddHours(2));
        db.EventJournals.Add(LostEvent(ChannelId, 1, T0, "Song A", "viewer_one"));
        await db.SaveChangesAsync();
        ActionRequiredInboxService sut = ActionRequiredInboxHarness.Create(db, clock);

        ActionRequiredItemDto first = (await sut.GetItemsAsync(ChannelId)).Value.Single();
        await sut.DismissAsync(ChannelId, Guid.NewGuid(), [first.Id]);
        (await sut.GetItemsAsync(ChannelId)).Value.Should().BeEmpty();

        db.EventJournals.Add(LostEvent(ChannelId, 2, T0.AddHours(1), "Song B", "viewer_two"));
        await db.SaveChangesAsync();

        ActionRequiredItemDto again = (await sut.GetItemsAsync(ChannelId)).Value.Single();
        again.Count.Should().Be(2);
        again.Parameters.Should().Contain("trackName", "Song B");
    }

    // ─── Undecryptable integration token ─────────────────────────────────────

    [Fact]
    public async Task AnUndecryptableToken_MarksItsConnectionForReauth_AndSurfacesIt()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await NewDbAsync();
        IntegrationConnection connection = new()
        {
            BroadcasterId = ChannelId,
            Provider = AuthEnums.IntegrationProvider.Spotify,
            Status = AuthEnums.IntegrationStatus.Connected,
        };
        db.IntegrationConnections.Add(connection);
        db.IntegrationTokens.Add(
            new IntegrationToken
            {
                ConnectionId = connection.Id,
                BroadcasterId = ChannelId,
                TokenType = AuthEnums.TokenType.Access,
                CipherText = "sealed-under-a-lost-key",
            }
        );
        await db.SaveChangesAsync();
        ITokenProtector protector = Substitute.For<ITokenProtector>();
        protector
            .TryUnprotectAsync(default!, default!)
            .ReturnsForAnyArgs(Task.FromResult<string?>(null));
        IEventBus bus = Substitute.For<IEventBus>();
        IntegrationTokenVault vault = new(
            db,
            protector,
            Substitute.For<ISubjectKeyService>(),
            Substitute.For<IScopeGrantService>(),
            bus,
            new FakeTimeProvider(T0),
            NullLogger<IntegrationTokenVault>.Instance
        );

        Result<DecryptedTokenDto> read = await vault.GetAccessTokenAsync(connection.Id);
        await vault.GetAccessTokenAsync(connection.Id);

        read.ErrorCode.Should().Be("DECRYPT_FAILED");
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<IntegrationNeedsReauthEvent>(e =>
                    e.ConnectionId == connection.Id && e.BroadcasterId == ChannelId
                ),
                Arg.Any<CancellationToken>()
            );
        db.ChangeTracker.Clear();
        ActionRequiredItemDto item = (await ItemsAsync(db)).Should().ContainSingle().Subject;
        item.Kind.Should().Be("integration_token_dead");
        item.MessageKey.Should().Be("attention_integration_unusable_message");
        item.Id.Should().Be($"token:{connection.Id}:{T0.Ticks}");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<ActionRequiredInboxServiceTestDbContext> NewDbAsync()
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        db.Channels.AddRange(NewChannel(ChannelId, "mine"), NewChannel(OtherChannelId, "other"));
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<List<ActionRequiredItemDto>> ItemsAsync(
        ActionRequiredInboxServiceTestDbContext db
    ) => (await ActionRequiredInboxHarness.Create(db).GetItemsAsync(ChannelId)).Value;

    private static Channel NewChannel(Guid id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            NameNormalized = name,
        };

    private static EventSubSubscription Refused(
        Guid channelId,
        string eventType,
        string? lastError
    ) =>
        new()
        {
            BroadcasterId = channelId,
            EventType = eventType,
            Version = "1",
            Status = "failed",
            LastError = lastError,
        };

    private static Widget NewWidget(Guid channelId, string name) =>
        new() { BroadcasterId = channelId, Name = name };

    private static WidgetVersion NewVersion(Widget widget, int number, string buildStatus) =>
        new()
        {
            WidgetId = widget.Id,
            BroadcasterId = widget.BroadcasterId,
            VersionNumber = number,
            BuildStatus = buildStatus,
            CreatedAt = T0.AddMinutes(number),
        };

    private static OutboundWebhookEndpoint NewEndpoint(Guid channelId, string name) =>
        new()
        {
            BroadcasterId = channelId,
            Name = name,
            Fqdn = "hooks.example.com",
            SigningSecretEnvelope = "sealed",
            EncryptionKeyId = Guid.NewGuid(),
            IsEnabled = true,
            CreatedAt = T0.AddDays(-7),
        };

    private static EventJournal LostEvent(
        Guid channelId,
        long position,
        DateTime occurredAt,
        string track,
        string requester
    ) =>
        new()
        {
            EventId = Guid.NewGuid(),
            BroadcasterId = channelId,
            StreamPosition = position,
            EventType = "SongRequestLostAtProviderEvent",
            EventVersion = 1,
            Source = "domain",
            Payload =
                $"{{\"TrackUri\":\"spotify:track:1\",\"TrackName\":\"{track}\",\"RequestedBy\":\"{requester}\"}}",
            Metadata = "{}",
            OccurredAt = occurredAt,
            RecordedAt = occurredAt,
        };
}
