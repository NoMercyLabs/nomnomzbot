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

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Application.ViewerData.Dtos;
using NomNomzBot.Application.ViewerData.Services;
using NomNomzBot.Application.Vts.Dtos;
using NomNomzBot.Application.Vts.Services;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Commands.PipelineOptions;
using NomNomzBot.Infrastructure.Identity.Pipeline;
using NomNomzBot.Infrastructure.Tests.Platform.Pipeline;
using NomNomzBot.Infrastructure.Tests.ViewerData;
using NomNomzBot.Infrastructure.Tts.Pipeline;
using NomNomzBot.Infrastructure.ViewerData.Pipeline;
using NomNomzBot.Infrastructure.Vts.Pipeline;
using NomNomzBot.Infrastructure.Webhooks.Pipeline;
using NomNomzBot.Infrastructure.Widgets.Pipeline;
using NSubstitute;
using InfraViewerDataService = NomNomzBot.Infrastructure.ViewerData.ViewerDataService;

namespace NomNomzBot.Infrastructure.Tests.PickerSources;

/// <summary>
/// The pickers for the names a step types today: counter keys, viewer-data keys, permit roles and capabilities,
/// event names, widget and webhook event types, VTube Studio request names and art mesh tags, and TTS speakers.
/// Each provider answers a list, a search, a page, a saved-value lookup and, where it reads a channel's data, never
/// shows another channel's rows.
/// </summary>
public sealed class NamedThingPickerProviderTests
{
    private static readonly Guid Broadcaster = Guid.NewGuid();
    private static readonly Guid OtherBroadcaster = Guid.NewGuid();

    // ── counter key ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Counter_key_lists_this_channels_counters_with_the_value_and_hides_other_channels()
    {
        ViewerDataTestDbContext db = ViewerDataTestDbContext.New();
        NamedCounterService counters = new(db);
        await counters.SetAsync(Broadcaster, "wins", 7);
        await counters.SetAsync(Broadcaster, "deaths", 3);
        await counters.SetAsync(OtherBroadcaster, "secret", 1);
        CounterKeyOptionProvider provider = new(counters);

        Result<PipelineOptionListResult> all = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of()
        );
        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "WIN")
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "deaths")
        );
        Result<PipelineOptionListResult> missing = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "secret")
        );
        Result<PipelineOptionListResult> second = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(page: new PaginationParams(Page: 2, PageSize: 1))
        );

        Assert.Equal(PipelineActionFieldKind.CounterKey, provider.Kind);
        Assert.Equal(["deaths", "wins"], PickerAsk.Values(all));
        Assert.Equal("Value: 3", all.Value.Items[0].SecondaryText);
        Assert.Equal(2, all.Value.TotalCount);
        Assert.Equal(["wins"], PickerAsk.Values(searched));
        Assert.Equal(["deaths"], PickerAsk.Values(looked));
        Assert.Empty(missing.Value.Items);
        Assert.Equal(["wins"], PickerAsk.Values(second));
    }

    [Fact]
    public async Task Counter_key_is_unavailable_with_the_reason_when_the_counters_cannot_be_read()
    {
        INamedCounterService counters = Substitute.For<INamedCounterService>();
        counters
            .ListAsync(
                Broadcaster,
                Arg.Any<string?>(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<PagedList<NamedCounterSummary>>("Counters are offline.", "X"));

        Result<PipelineOptionListResult> result = await new CounterKeyOptionProvider(
            counters
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());

        PickerAsk.AssertUnavailable(result, "Counters are offline.");
    }

    // ── viewer data key ──────────────────────────────────────────────────────

    [Fact]
    public async Task Viewer_data_key_lists_this_channels_keys_with_the_viewer_count_and_hides_other_channels()
    {
        ViewerDataTestDbContext db = ViewerDataTestDbContext.New();
        InfraViewerDataService data = new(db, TimeProvider.System);
        Guid first = await KeyListServiceTests.SeedViewerAsync(db);
        Guid other = await KeyListServiceTests.SeedViewerAsync(db);
        await data.SetAsync(Broadcaster, first, "deaths", "1");
        await data.SetAsync(Broadcaster, other, "deaths", "2");
        await data.SetAsync(Broadcaster, first, "quest", "a");
        await data.SetAsync(OtherBroadcaster, first, "secret", "x");
        ViewerDataKeyOptionProvider provider = new(data);

        Result<PipelineOptionListResult> all = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of()
        );
        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "que")
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "deaths")
        );
        Result<PipelineOptionListResult> missing = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "secret")
        );
        Result<PipelineOptionListResult> second = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(page: new PaginationParams(Page: 2, PageSize: 1))
        );

        Assert.Equal(PipelineActionFieldKind.ViewerDataKey, provider.Kind);
        Assert.Equal(["deaths", "quest"], PickerAsk.Values(all));
        Assert.Equal("2 viewers", all.Value.Items[0].SecondaryText);
        Assert.Equal("1 viewer", all.Value.Items[1].SecondaryText);
        Assert.Equal(["quest"], PickerAsk.Values(searched));
        Assert.Equal(["deaths"], PickerAsk.Values(looked));
        Assert.Empty(missing.Value.Items);
        Assert.Equal(["quest"], PickerAsk.Values(second));
        Assert.Equal(2, second.Value.TotalCount);
    }

    [Fact]
    public async Task Viewer_data_key_is_unavailable_with_the_reason_when_the_keys_cannot_be_read()
    {
        IViewerDataService data = Substitute.For<IViewerDataService>();
        data.ListKeysAsync(
                Broadcaster,
                Arg.Any<string?>(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<PagedList<ViewerDataKeySummary>>("Viewer data is offline.", "X")
            );

        Result<PipelineOptionListResult> result = await new ViewerDataKeyOptionProvider(
            data
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());

        PickerAsk.AssertUnavailable(result, "Viewer data is offline.");
    }

    // ── permit token ─────────────────────────────────────────────────────────

    private static ActionPermissionDto Capability(
        string key,
        string? description,
        bool grantable
    ) =>
        new(
            Guid.NewGuid(),
            key,
            AuthPlane.Management,
            description,
            10,
            10,
            DangerTier.Low,
            grantable,
            null,
            10
        );

    private static IActionAuthorizationService Matrix(params ActionPermissionDto[] actions)
    {
        IActionAuthorizationService service = Substitute.For<IActionAuthorizationService>();
        service
            .GetActionMatrixAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<ActionPermissionDto>>(actions));
        return service;
    }

    [Fact]
    public async Task Permit_token_lists_the_roles_then_only_the_grantable_capabilities()
    {
        PermitTokenOptionProvider provider = new(
            Matrix(
                Capability("chat:send_as_bot", "Speak as the bot", grantable: true),
                Capability("economy:config:write", "Edit the economy", grantable: false)
            )
        );

        Result<PipelineOptionListResult> all = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of()
        );
        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "bot")
        );
        Result<PipelineOptionListResult> second = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(page: new PaginationParams(Page: 2, PageSize: 4))
        );

        Assert.Equal(PipelineActionFieldKind.PermitToken, provider.Kind);
        Assert.Equal(
            ["moderator", "leadmoderator", "editor", "broadcaster", "chat:send_as_bot"],
            PickerAsk.Values(all)
        );
        Assert.Equal("Role", all.Value.Items[0].SecondaryText);
        Assert.Equal("Speak as the bot", all.Value.Items[4].SecondaryText);
        Assert.Equal(["chat:send_as_bot"], PickerAsk.Values(searched));
        Assert.Equal(["chat:send_as_bot"], PickerAsk.Values(second));
        Assert.Equal(5, second.Value.TotalCount);
    }

    [Fact]
    public async Task Permit_token_finds_a_role_by_the_alias_a_chat_command_accepts()
    {
        PermitTokenOptionProvider provider = new(Matrix());

        Result<PipelineOptionListResult> mod = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "mod")
        );
        Result<PipelineOptionListResult> capability = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "no:such")
        );

        Assert.Equal(["moderator"], PickerAsk.Values(mod));
        Assert.Empty(capability.Value.Items);
    }

    [Fact]
    public async Task Permit_token_is_unavailable_with_the_reason_when_the_matrix_cannot_be_read()
    {
        IActionAuthorizationService service = Substitute.For<IActionAuthorizationService>();
        service
            .GetActionMatrixAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<ActionPermissionDto>>("Matrix is offline.", "X"));

        Result<PipelineOptionListResult> result = await new PermitTokenOptionProvider(
            service
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());

        PickerAsk.AssertUnavailable(result, "Matrix is offline.");
    }

    // ── event name ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Event_name_lists_the_catalogue_plus_this_channels_own_event_types_only()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.EventResponses.AddRange(
            new EventResponse { BroadcasterId = Broadcaster, EventType = "custom.my_source" },
            new EventResponse { BroadcasterId = Broadcaster, EventType = "channel.follow" },
            new EventResponse { BroadcasterId = OtherBroadcaster, EventType = "custom.theirs" }
        );
        await db.SaveChangesAsync();
        EventNameOptionProvider provider = new(db);

        Result<PipelineOptionListResult> follow = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "channel.follow")
        );
        Result<PipelineOptionListResult> custom = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "custom.")
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "stream.online")
        );
        Result<PipelineOptionListResult> page = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(page: new PaginationParams(Page: 2, PageSize: 10))
        );

        Assert.Equal(PipelineActionFieldKind.EventName, provider.Kind);
        Assert.Equal(["channel.follow"], PickerAsk.Values(follow));
        Assert.Equal("Event", follow.Value.Items[0].SecondaryText);
        Assert.Equal(["custom.my_source"], PickerAsk.Values(custom));
        Assert.Equal("Added on this channel", custom.Value.Items[0].SecondaryText);
        Assert.Equal(["stream.online"], PickerAsk.Values(looked));
        Assert.Equal(10, page.Value.Items.Count);
        Assert.DoesNotContain("custom.theirs", PickerAsk.Values(page));
    }

    // ── widget event type ────────────────────────────────────────────────────

    private static Widget WidgetOf(Guid owner, string name, params string[] types) =>
        new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = owner,
            Name = name,
            Framework = "vue",
            Source = "custom",
            EventSubscriptions = [.. types],
        };

    [Fact]
    public async Task Widget_event_type_lists_the_types_the_widgets_listen_to_and_narrows_by_widget_id()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        Widget alerts = WidgetOf(
            Broadcaster,
            "Alerts",
            "FollowEvent",
            "RaidEvent",
            Widget.WildcardEvent
        );
        Widget chat = WidgetOf(Broadcaster, "Chat", "RaidEvent", "ChatMessage");
        Widget foreign = WidgetOf(OtherBroadcaster, "Theirs", "SecretEvent");
        db.Widgets.AddRange(alerts, chat, foreign);
        await db.SaveChangesAsync();
        WidgetEventTypeOptionProvider provider = new(db);

        Result<PipelineOptionListResult> all = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of()
        );
        Result<PipelineOptionListResult> narrowed = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(
                null,
                null,
                PickerAsk.FirstPage,
                new Dictionary<string, string> { ["widget_id"] = alerts.Id.ToString() }
            )
        );
        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "message")
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "RaidEvent")
        );

        Assert.Equal(PipelineActionFieldKind.WidgetEventType, provider.Kind);
        Assert.Equal(["ChatMessage", "FollowEvent", "RaidEvent"], PickerAsk.Values(all));
        Assert.Equal("Alerts, Chat", all.Value.Items[2].SecondaryText);
        Assert.Equal(["FollowEvent", "RaidEvent"], PickerAsk.Values(narrowed));
        Assert.Equal(["ChatMessage"], PickerAsk.Values(searched));
        Assert.Equal(["RaidEvent"], PickerAsk.Values(looked));
        Assert.DoesNotContain("SecretEvent", PickerAsk.Values(all));
    }

    [Fact]
    public async Task Widget_event_type_pages_its_list()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.Widgets.Add(WidgetOf(Broadcaster, "Alerts", "A", "B", "C"));
        await db.SaveChangesAsync();

        Result<PipelineOptionListResult> second = await new WidgetEventTypeOptionProvider(
            db
        ).GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(page: new PaginationParams(Page: 2, PageSize: 2))
        );

        Assert.Equal(["C"], PickerAsk.Values(second));
        Assert.Equal(3, second.Value.TotalCount);
    }

    // ── webhook event type ───────────────────────────────────────────────────

    private static IOutboundWebhookEndpointService Catalogue(
        params OutboundWebhookEventCatalogueEntry[] entries
    )
    {
        IOutboundWebhookEndpointService service = Substitute.For<IOutboundWebhookEndpointService>();
        service
            .GetEventCatalogue()
            .Returns(Result.Success<IReadOnlyList<OutboundWebhookEventCatalogueEntry>>(entries));
        return service;
    }

    [Fact]
    public async Task Webhook_event_type_lists_the_subscribable_catalogue_with_the_category()
    {
        WebhookEventTypeOptionProvider provider = new(
            Catalogue(
                new(
                    "FollowEvent",
                    "label_follow",
                    "webhook_event_category_followers_subscriptions"
                ),
                new("ChannelOnlineEvent", "label_online", "webhook_event_category_stream"),
                new("RaidEvent", "label_raid", "webhook_event_category_raids_shoutouts")
            )
        );

        Result<PipelineOptionListResult> all = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of()
        );
        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "followers")
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "RaidEvent")
        );
        Result<PipelineOptionListResult> second = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(page: new PaginationParams(Page: 2, PageSize: 2))
        );

        Assert.Equal(PipelineActionFieldKind.WebhookEventType, provider.Kind);
        Assert.Equal(["ChannelOnlineEvent", "FollowEvent", "RaidEvent"], PickerAsk.Values(all));
        Assert.Equal("followers subscriptions", all.Value.Items[1].SecondaryText);
        Assert.Equal(["FollowEvent"], PickerAsk.Values(searched));
        Assert.Equal(["RaidEvent"], PickerAsk.Values(looked));
        Assert.Equal(["RaidEvent"], PickerAsk.Values(second));
    }

    [Fact]
    public async Task Webhook_event_type_is_unavailable_with_the_reason_when_the_catalogue_cannot_be_read()
    {
        IOutboundWebhookEndpointService service = Substitute.For<IOutboundWebhookEndpointService>();
        service
            .GetEventCatalogue()
            .Returns(
                Result.Failure<IReadOnlyList<OutboundWebhookEventCatalogueEntry>>(
                    "Catalogue is offline.",
                    "X"
                )
            );

        Result<PipelineOptionListResult> result = await new WebhookEventTypeOptionProvider(
            service
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());

        PickerAsk.AssertUnavailable(result, "Catalogue is offline.");
    }

    // ── VTube Studio request type and art mesh tag ───────────────────────────

    [Fact]
    public async Task Vts_request_type_lists_the_documented_requests_in_documentation_order()
    {
        VtsRequestTypeOptionProvider provider = new();

        Result<PipelineOptionListResult> all = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(page: new PaginationParams(Page: 1, PageSize: 100))
        );
        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "artmesh")
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "HotkeyTriggerRequest")
        );
        Result<PipelineOptionListResult> unknown = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "NotARequest")
        );
        Result<PipelineOptionListResult> second = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(page: new PaginationParams(Page: 2, PageSize: 10))
        );

        Assert.Equal(PipelineActionFieldKind.VtsRequestType, provider.Kind);
        Assert.Equal(37, all.Value.TotalCount);
        Assert.Equal("APIStateRequest", all.Value.Items[0].Value);
        Assert.Contains("ColorTintRequest", PickerAsk.Values(all));
        Assert.Equal(
            ["ArtMeshListRequest", "ArtMeshAtPositionRequest", "ArtMeshSelectionRequest"],
            PickerAsk.Values(searched)
        );
        Assert.Equal(["HotkeyTriggerRequest"], PickerAsk.Values(looked));
        Assert.Empty(unknown.Value.Items);
        Assert.Equal(10, second.Value.Items.Count);
    }

    [Fact]
    public async Task Vts_art_mesh_tag_lists_the_tags_of_the_loaded_model()
    {
        IVtsControlService vts = Substitute.For<IVtsControlService>();
        vts.GetInventoryAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new VtsModelInventory([], [], [], ["Hair", "Cheeks", "Hat"])));
        VtsArtMeshTagOptionProvider provider = new(vts);

        Result<PipelineOptionListResult> all = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of()
        );
        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "ha")
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "Cheeks")
        );
        Result<PipelineOptionListResult> second = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(page: new PaginationParams(Page: 2, PageSize: 2))
        );

        Assert.Equal(PipelineActionFieldKind.VtsArtMeshTag, provider.Kind);
        Assert.Equal(["Cheeks", "Hair", "Hat"], PickerAsk.Values(all));
        Assert.Equal(["Hair", "Hat"], PickerAsk.Values(searched));
        Assert.Equal(["Cheeks"], PickerAsk.Values(looked));
        Assert.Equal(["Hat"], PickerAsk.Values(second));
    }

    [Fact]
    public async Task Vts_art_mesh_tag_is_unavailable_with_the_reason_when_vts_is_not_connected()
    {
        IVtsControlService vts = Substitute.For<IVtsControlService>();
        vts.GetInventoryAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<VtsModelInventory>("VTube Studio is not connected.", "X"));

        Result<PipelineOptionListResult> result = await new VtsArtMeshTagOptionProvider(
            vts
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());

        PickerAsk.AssertUnavailable(result, "VTube Studio is not connected.");
    }

    // ── TTS speaker ──────────────────────────────────────────────────────────

    private static async Task SeedViewerAsync(
        PipelineOptionsTestDbContext db,
        Guid owner,
        string twitchId,
        string name
    )
    {
        db.ViewerProfiles.Add(
            new ViewerProfile
            {
                Id = Guid.NewGuid(),
                BroadcasterId = owner,
                ViewerUserId = Guid.NewGuid(),
                ViewerTwitchUserId = twitchId,
                UsernameSnapshot = name,
                DisplayNameSnapshot = name,
                LastSeenAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Tts_speaker_lists_the_closed_words_first_then_this_channels_viewers()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        await SeedViewerAsync(db, Broadcaster, "111", "Grimlock");
        await SeedViewerAsync(db, OtherBroadcaster, "222", "Stranger");
        TtsSpeakerOptionProvider provider = new(db);

        Result<PipelineOptionListResult> all = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of()
        );
        Result<PipelineOptionListResult> searchedWord = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "broad")
        );
        Result<PipelineOptionListResult> searchedViewer = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "grim")
        );
        Result<PipelineOptionListResult> word = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "bot")
        );
        Result<PipelineOptionListResult> viewer = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "111")
        );
        Result<PipelineOptionListResult> second = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(page: new PaginationParams(Page: 2, PageSize: 1))
        );

        Assert.Equal(PipelineActionFieldKind.TtsSpeaker, provider.Kind);
        Assert.Equal(["bot", "viewer", "user", "broadcaster", "111"], PickerAsk.Values(all));
        Assert.Equal(5, all.Value.TotalCount);
        Assert.Equal(["broadcaster"], PickerAsk.Values(searchedWord));
        Assert.Equal(["111"], PickerAsk.Values(searchedViewer));
        Assert.Equal(["bot"], PickerAsk.Values(word));
        Assert.Equal(["111"], PickerAsk.Values(viewer));
        Assert.DoesNotContain("222", PickerAsk.Values(all));
        Assert.Empty(second.Value.Items);
    }
}
