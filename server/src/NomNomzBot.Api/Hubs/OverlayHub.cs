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

using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Hubs.Overlay;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Domain.Widgets.Events;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Api.Hubs;

public class OverlayHub : Hub<IOverlayClient>
{
    // The widget subscription key WidgetNowPlayingHandler pushes under (WidgetAlertHandlers.cs) — a widget
    // that never subscribed to it renders no music state, so its connection has nothing to gain from a fast
    // poll cadence.
    private const string NowPlayingEventKey = "now_playing";
    private const string MusicNowPlayingEventKey = "music.nowPlaying";
    private const string WrongWidgetError = "This overlay token is scoped to a different widget";
    private const string YouTubePlayingItem = "YouTubePlaying";

    private readonly IApplicationDbContext _db;
    private readonly IWidgetService _widgetService;
    private readonly IOverlayTicketService _tickets;
    private readonly OverlayPresenceRegistry _presence;
    private readonly IChannelRegistry _registry;
    private readonly IActionRequiredChangeNotifier _inbox;
    private readonly IEventBus _eventBus;
    private readonly IReadOnlyList<IWidgetSeedProvider> _seedProviders;
    private readonly ILogger<OverlayHub> _logger;

    public OverlayHub(
        IApplicationDbContext db,
        IWidgetService widgetService,
        IOverlayTicketService tickets,
        OverlayPresenceRegistry presence,
        IChannelRegistry registry,
        IActionRequiredChangeNotifier inbox,
        IEventBus eventBus,
        IEnumerable<IWidgetSeedProvider> seedProviders,
        ILogger<OverlayHub> logger
    )
    {
        _seedProviders = seedProviders.ToList();
        _db = db;
        _widgetService = widgetService;
        _tickets = tickets;
        _presence = presence;
        _registry = registry;
        _inbox = inbox;
        _eventBus = eventBus;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        // The long-lived overlay token never rides on this URL (S035 item 3, U·B5/B7): the SDK exchanges it
        // for a short-lived, single-use ticket via POST /overlay/ticket (header, not query string) first, and
        // only that ticket appears here.
        string? ticket = Context.GetHttpContext()?.Request.Query["ticket"].ToString();
        OverlayTokenScope? scope = _tickets.RedeemTicket(ticket);
        if (scope is null)
        {
            Context.Abort();
            return;
        }

        Context.Items["BroadcasterId"] = scope.BroadcasterId;
        // A widget-scoped ticket (audit S-OVERLAY-1: one widget's leaked token must never unlock another
        // widget on the same connection) confines every later JoinWidget call to exactly this widget.
        Context.Items["WidgetId"] = scope.WidgetId;
        // All overlay connections for a broadcaster share the overlay group so sound play/stop
        // signals (and future broadcaster-wide overlay events) reach every browser source.
        string overlayGroup = OverlayPresenceRegistry.OverlayGroupName(scope.BroadcasterId);
        await Groups.AddToGroupAsync(Context.ConnectionId, overlayGroup);
        // Tracked in the same presence registry as widget attachment, so IOverlayPresenceRegistry can
        // answer "is any browser source connected at all" for features (sound-clip stop) that push to the
        // shared bus rather than one specific widget.
        _presence.Attach(Context.ConnectionId, overlayGroup);
        _presence.RegisterOverlay(Context.ConnectionId, scope.BroadcasterId);
        // A widget token can be rotated later: remember which one this connection came in on.
        if (scope is { WidgetId: { } scopedWidget, Token: { } widgetToken })
            _presence.BindToken(Context.ConnectionId, scopedWidget, widgetToken, Context);
        // A widget gets its events as WidgetEvent, by subscription. Only a page that hosts no widget reads
        // the generic feed; JoinWidget takes a channel-wide connection off it again.
        if (scope.WidgetId is null)
        {
            string feedGroup = OverlayPresenceRegistry.FeedGroupName(scope.BroadcasterId);
            await Groups.AddToGroupAsync(Context.ConnectionId, feedGroup);
            _presence.Attach(Context.ConnectionId, feedGroup);
        }
        _inbox.NotifyChanged(scope.BroadcasterId);
        _logger.LogDebug("Overlay connected for channel {B}", scope.BroadcasterId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Guid? channel = Context.Items["BroadcasterId"] as Guid?;
        foreach (string groupName in _presence.Drop(Context.ConnectionId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            if (
                channel is Guid owner
                && OverlayPresenceRegistry.TryParseWidgetId(owner, groupName, out Guid widgetId)
            )
                await PublishWidgetDisconnectedAsync(owner, widgetId);
        }
        if (channel is Guid broadcasterId)
        {
            _registry.ReleaseMusicDemand(broadcasterId, Context.ConnectionId);
            _inbox.NotifyChanged(broadcasterId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// The join an SDK that predates the single audio route sends. That SDK still plays every widget event it
    /// receives, so it would double the sound: join it, then make it reload to pick up the current SDK.
    /// </summary>
    public async Task<JoinWidgetResponse> JoinWidget(string widgetId)
    {
        JoinWidgetResponse response = await JoinWidgetWithSdk(widgetId, string.Empty);
        await Clients.Caller.WidgetReload();
        return response;
    }

    public async Task<JoinWidgetResponse> JoinWidgetWithSdk(string widgetId, string sdkVersion)
    {
        if (Context.Items["BroadcasterId"] is not Guid broadcasterId)
            return new(false, "Not authenticated", null);

        // A widget-scoped connection (audit S-OVERLAY-1) may only ever join its OWN widget — never another
        // one on the same channel, even though the broadcaster id matches. Channel-wide connections (the
        // WidgetId scope item is null) keep joining any widget id, as before.
        if (!MayActFor(widgetId))
            return new(false, WrongWidgetError, null);

        string groupName = OverlayPresenceRegistry.GroupName(broadcasterId, widgetId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        bool newlyJoined = _presence.Attach(Context.ConnectionId, groupName);
        if (newlyJoined && Guid.TryParse(widgetId, out Guid connectedWidgetId))
            await _eventBus.PublishAsync(
                new WidgetConnectedEvent
                {
                    BroadcasterId = broadcasterId,
                    WidgetId = connectedWidgetId,
                    ConnectionId = Context.ConnectionId,
                },
                Context.ConnectionAborted
            );
        string feedGroup = OverlayPresenceRegistry.FeedGroupName(broadcasterId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, feedGroup);
        _presence.Detach(Context.ConnectionId, feedGroup);
        _logger.LogDebug(
            "Overlay connection {C} joined widget {W}",
            Context.ConnectionId,
            widgetId
        );

        // Hand the browser-source its saved appearance settings up front, so it can style itself
        // before the first event arrives (the page applies the keys it understands, ignores the rest).
        Widget? widget = Guid.TryParse(widgetId, out Guid parsedWidgetId)
            ? await _db
                .Widgets.AsNoTracking()
                .FirstOrDefaultAsync(w =>
                    w.Id == parsedWidgetId && w.BroadcasterId == broadcasterId
                )
            : null;

        string? naturalKey = await NaturalKeyAsync(widget);
        if (WidgetKeyAliases.Canonical(naturalKey) == OverlayPresenceRegistry.AudioSourceNaturalKey)
        {
            string? previousAudio = _presence.GetAudioTarget(broadcasterId);
            Guid? previousWidget = _presence.GetAudioWidgetId(broadcasterId);
            _presence.MarkAudioSource(Context.ConnectionId, widget!.Id);
            if (
                previousAudio is not null
                && previousAudio != Context.ConnectionId
                && previousWidget is not null
            )
            {
                await Clients
                    .Client(previousAudio)
                    .WidgetEvent(
                        new WidgetEventDto(
                            previousWidget.Value.ToString(),
                            "youtube.stop",
                            new { handoff = true }
                        )
                    );
            }
            _inbox.NotifyChanged(broadcasterId);
        }
        else if (naturalKey == OverlayPresenceRegistry.TtsSourceNaturalKey)
        {
            _presence.MarkTtsSource(Context.ConnectionId);
            _inbox.NotifyChanged(broadcasterId);
        }
        else if (WidgetKeyAliases.Canonical(naturalKey) == WidgetKeyAliases.MusicPlayer)
        {
            (string ConnectionId, Guid? WidgetId)? displaced = _presence.MarkMusicSource(
                Context.ConnectionId,
                widget!.Id
            );
            if (displaced is { WidgetId: not null } previous)
            {
                _logger.LogWarning(
                    "duplicate_output: a second Music Player page joined channel {B}; {C} replaces {P}",
                    broadcasterId,
                    Context.ConnectionId,
                    previous.ConnectionId
                );
                await Clients
                    .Client(previous.ConnectionId)
                    .WidgetEvent(
                        new WidgetEventDto(
                            previous.WidgetId.Value.ToString(),
                            "youtube.stop",
                            new { handoff = true }
                        )
                    );
            }
            _inbox.NotifyChanged(broadcasterId);
        }

        // A real browser source just (re)connected — proof the widget is alive right now, so a fault stamped
        // by a past session (S-PL1: e.g. a one-off autoplay block on first load) must not keep painting the
        // dashboard row red forever. ReportRuntimeError re-stamps it below if the fault is still live.
        if (widget is not null)
            await _widgetService.ClearRuntimeErrorAsync(broadcasterId.ToString(), widgetId);

        // A now-playing widget just came alive on stream — keep the music poller at its fast cadence for
        // this channel for as long as this connection is here (see IChannelRegistry.TouchMusicDemand).
        if (widget is not null && WantsMusic(widget))
            _registry.TouchMusicDemand(broadcasterId, Context.ConnectionId);

        return new(
            true,
            null,
            await EffectiveSettingsAsync(widget, broadcasterId),
            await SeedFramesAsync(widget, naturalKey, broadcasterId)
        );
    }

    /// <summary>
    /// The SDK reports the event types its page registered (every <c>on(type)</c>, and <c>"*"</c> for
    /// <c>onAny</c>) after a join and whenever the set grows. The widget's stored routing list becomes its
    /// catalogue default (a first-party widget's server-side events, such as a redemption sound) joined with the
    /// subscribed set — never the old stored list — so the widget gets exactly those events and no others.
    /// Refused (false, nothing stored) for a widget the connection may not act for, an unknown widget, or any
    /// invalid name.
    /// </summary>
    public async Task<bool> Subscribe(string widgetId, string[] events)
    {
        if (Context.Items["BroadcasterId"] is not Guid broadcasterId)
            return false;
        if (!MayActFor(widgetId) || !Guid.TryParse(widgetId, out Guid parsedWidgetId))
            return false;
        if (!WidgetEventDeclaration.TryNormalize(events, out List<string> subscribed))
            return false;

        Widget? widget = await _db.Widgets.FirstOrDefaultAsync(
            w => w.Id == parsedWidgetId && w.BroadcasterId == broadcasterId,
            Context.ConnectionAborted
        );
        if (widget is null)
            return false;

        List<string> defaults = await CatalogueDefaultsAsync(widget);
        List<string> routed = [.. defaults, .. subscribed.Except(defaults)];
        if (!routed.ToHashSet().SetEquals(widget.EventSubscriptions))
        {
            widget.EventSubscriptions = routed;
            await _db.SaveChangesAsync(Context.ConnectionAborted);
            await _eventBus.PublishAsync(
                new ChannelConfigChangedEvent
                {
                    BroadcasterId = broadcasterId,
                    Domain = "widgets",
                    EntityId = widget.Id.ToString(),
                    Action = "updated",
                },
                Context.ConnectionAborted
            );
        }

        if (WantsMusic(widget))
            _registry.TouchMusicDemand(broadcasterId, Context.ConnectionId);
        else
            _registry.ReleaseMusicDemand(broadcasterId, Context.ConnectionId);
        return true;
    }

    // The events a first-party widget gets whatever its page registers (the server acts on them, such as the
    // redemption sound). A custom widget, or a gallery row that is not first-party, has none: a community
    // submission can never claim a catalogue entry by its natural key.
    private async Task<List<string>> CatalogueDefaultsAsync(Widget widget)
    {
        if (widget.GalleryItemId is not Guid galleryItemId)
            return [];
        string? naturalKey = await _db
            .WidgetGalleryItems.AsNoTracking()
            .Where(i => i.Id == galleryItemId && i.TrustTier == "first_party")
            .Select(i => i.NaturalKey)
            .FirstOrDefaultAsync(Context.ConnectionAborted);
        return [.. FirstPartyWidgetCatalogue.Find(naturalKey)?.DefaultEventSubscriptions ?? []];
    }

    // A widget that renders music state keeps the poller on its fast cadence while its page is open.
    private static bool WantsMusic(Widget widget) =>
        widget.IsEnabled
        && (widget.Wants(NowPlayingEventKey) || widget.Wants(MusicNowPlayingEventKey));

    // Runs on every join, so a reconnect heals too. A provider that fails costs the widget its seed, never the join.
    private async Task<IReadOnlyList<WidgetSeedFrame>> SeedFramesAsync(
        Widget? widget,
        string? naturalKey,
        Guid broadcasterId
    )
    {
        if (widget is null || naturalKey is null)
            return [];
        IWidgetSeedProvider? provider = _seedProviders.FirstOrDefault(p =>
            p.NaturalKey == naturalKey
        );
        if (provider is null)
            return [];
        try
        {
            return await provider.SeedAsync(broadcasterId, widget, Context.ConnectionAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Seed provider {Key} failed for widget {W}",
                naturalKey,
                widget.Id
            );
            return [];
        }
    }

    // What the page receives is the saved bag plus the defaults its settings.json declares; if the lookup fails
    // the saved bag is still better than nothing.
    private async Task<object?> EffectiveSettingsAsync(Widget? widget, Guid broadcasterId)
    {
        if (widget is null)
            return null;
        Result<Dictionary<string, object>> effective =
            await _widgetService.GetEffectiveSettingsAsync(
                broadcasterId,
                widget.Id,
                Context.ConnectionAborted
            );
        return effective.IsSuccess ? effective.Value : widget.Settings;
    }

    private async Task<string?> NaturalKeyAsync(Widget? widget)
    {
        if (widget?.GalleryItemId is not Guid galleryItemId)
            return null;
        return await _db
            .WidgetGalleryItems.AsNoTracking()
            .Where(i => i.Id == galleryItemId)
            .Select(i => i.NaturalKey)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Runs one pipeline action for a widget, as the channel owner (widget-sdk.md §8): the SDK's
    /// <c>actions.invoke</c> -> here. Only the widget the token names may act, and only within the owner's IAM.
    /// </summary>
    public async Task<WidgetActionResponse> InvokeAction(
        string widgetId,
        string actionType,
        Dictionary<string, JsonElement>? parameters,
        Dictionary<string, string>? variables,
        [FromServices] IWidgetActionService actions
    )
    {
        if (Context.Items["BroadcasterId"] is not Guid broadcasterId)
            return Refused("Not authenticated", "AUTH_REQUIRED");
        if (!MayActFor(widgetId) || !Guid.TryParse(widgetId, out Guid parsedWidgetId))
            return Refused(WrongWidgetError, "FORBIDDEN");

        Result<WidgetActionOutcome> outcome = await actions.InvokeAsync(
            new(broadcasterId, parsedWidgetId, actionType, parameters, variables),
            Context.ConnectionAborted
        );
        return outcome.IsSuccess
            ? new(
                outcome.Value.Succeeded,
                outcome.Value.Output,
                outcome.Value.Error,
                null,
                outcome.Value.Variables
            )
            : Refused(outcome.ErrorMessage ?? "Action refused", outcome.ErrorCode);
    }

    /// <summary>
    /// The SDK's <c>actions.claim</c>: true only for the first open copy of the widget to claim
    /// <paramref name="key"/>, so a widget open in two OBS sources acts on an event once.
    /// </summary>
    public async Task<bool> ClaimOnce(
        string widgetId,
        string key,
        [FromServices] IWidgetActionService actions
    )
    {
        if (Context.Items["BroadcasterId"] is not Guid broadcasterId)
            throw new HubException("Not authenticated");
        if (!MayActFor(widgetId) || !Guid.TryParse(widgetId, out Guid parsedWidgetId))
            throw new HubException(WrongWidgetError);

        Result<bool> claimed = await actions.ClaimAsync(
            broadcasterId,
            parsedWidgetId,
            key,
            Context.ConnectionAborted
        );
        return claimed.IsSuccess
            ? claimed.Value
            : throw new HubException(claimed.ErrorMessage ?? "Claim refused");
    }

    /// <summary>
    /// The selected Audio page reports what its YouTube player plays. Reports from other widget pages or
    /// channels are refused and store nothing.
    /// </summary>
    public async Task<bool> ReportYouTubePlayerState(
        string widgetId,
        string videoId,
        string state,
        long positionMs,
        [FromServices] IYouTubePlayerReportService reports
    )
    {
        if (Context.Items["BroadcasterId"] is not Guid broadcasterId)
            return false;
        if (!MayActFor(widgetId) || !Guid.TryParse(widgetId, out Guid parsedWidgetId))
            return false;
        if (
            _presence.GetMusicTarget(broadcasterId) != Context.ConnectionId
            || _presence.GetMusicWidgetId(broadcasterId) != parsedWidgetId
        )
            return false;

        bool ownsWidget = await _db
            .Widgets.AsNoTracking()
            .AnyAsync(
                w => w.Id == parsedWidgetId && w.BroadcasterId == broadcasterId,
                Context.ConnectionAborted
            );
        if (!ownsWidget)
            return false;

        Result reported = await reports.ReportAsync(
            broadcasterId,
            parsedWidgetId,
            videoId,
            state,
            positionMs,
            Context.ConnectionAborted
        );
        // This connection's page has a video on: WidgetReady then leaves it alone instead of reloading it.
        if (reported.IsSuccess && IsOnThePlayer(state))
            Context.Items[YouTubePlayingItem] = true;
        return reported.IsSuccess;
    }

    /// <summary>
    /// The media-share page reports its player. The report counts only from a widget of the connection's own
    /// channel that subscribes to <c>media_share_command</c>; an ended or error report moves the queue on.
    /// </summary>
    public async Task<bool> ReportMediaSharePlayerState(
        string widgetId,
        string requestId,
        string state,
        int positionSeconds,
        int durationSeconds,
        int volume,
        [FromServices] IMediaSharePlaybackService playback
    )
    {
        if (Context.Items["BroadcasterId"] is not Guid broadcasterId)
            return false;
        if (!MayActFor(widgetId) || !Guid.TryParse(widgetId, out Guid parsedWidgetId))
            return false;
        if (!IsMediaPlayerState(state))
            return false;

        List<Widget> own = await _db
            .Widgets.AsNoTracking()
            .Where(w => w.Id == parsedWidgetId && w.BroadcasterId == broadcasterId)
            .ToListAsync(Context.ConnectionAborted);
        if (!WidgetAlertRouting.Subscribers(own, "media_share_command").Any())
            return false;

        Guid? parsedRequestId = Guid.TryParse(requestId, out Guid request) ? request : null;
        Result reported = await playback.ReportStateAsync(
            broadcasterId,
            new(parsedRequestId, state, positionSeconds, durationSeconds, volume),
            Context.ConnectionAborted
        );
        return reported.IsSuccess;
    }

    private static bool IsMediaPlayerState(string state) =>
        state
            is MediaPlayerStates.Idle
                or MediaPlayerStates.Playing
                or MediaPlayerStates.Paused
                or MediaPlayerStates.Ended
                or MediaPlayerStates.Error;

    private static bool IsOnThePlayer(string state) =>
        state.Equals("playing", StringComparison.OrdinalIgnoreCase)
        || state.Equals("paused", StringComparison.OrdinalIgnoreCase);

    public async Task LeaveWidget(string widgetId)
    {
        if (Context.Items["BroadcasterId"] is not Guid broadcasterId)
            return;
        string groupName = OverlayPresenceRegistry.GroupName(broadcasterId, widgetId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        bool wasJoined = _presence.Detach(Context.ConnectionId, groupName);
        if (wasJoined && Guid.TryParse(widgetId, out Guid parsedWidgetId))
            await PublishWidgetDisconnectedAsync(broadcasterId, parsedWidgetId);
        _registry.ReleaseMusicDemand(broadcasterId, Context.ConnectionId);
    }

    private Task PublishWidgetDisconnectedAsync(Guid broadcasterId, Guid widgetId) =>
        _eventBus.PublishAsync(
            new WidgetDisconnectedEvent
            {
                BroadcasterId = broadcasterId,
                WidgetId = widgetId,
                ConnectionId = Context.ConnectionId,
            }
        );

    public async Task WidgetReady(string widgetId, [FromServices] IPlaybackInputSink playback)
    {
        _logger.LogDebug("Widget {W} ready on connection {C}", widgetId, Context.ConnectionId);
        if (
            Context.Items["BroadcasterId"] is not Guid broadcasterId
            || !MayActFor(widgetId)
            || !Guid.TryParse(widgetId, out Guid parsedWidgetId)
            || _presence.GetMusicTarget(broadcasterId) != Context.ConnectionId
            || _presence.GetMusicWidgetId(broadcasterId) != parsedWidgetId
        )
            return;

        // Nothing is pushed from here. The director starts the queue head when it is idle or blocked, and
        // tells the engine that holds a track to put it back on this page (a page that reloaded mid-video).
        bool pageIsPlaying =
            Context.Items.TryGetValue(YouTubePlayingItem, out object? playing) && playing is true;
        await playback.SendAsync(
            broadcasterId,
            new OutputAttached(pageIsPlaying),
            Context.ConnectionAborted
        );
    }

    /// <summary>
    /// An overlay-reported runtime fault for a widget (the SDK's <c>reportError</c> -> host -> here). Recorded
    /// against the widget (audit B5). Best-effort: an unparseable id or a widget the token's channel does not own is
    /// ignored by the service; the message is truncated so a looping widget cannot bloat the row.
    /// </summary>
    public async Task ReportRuntimeError(string widgetId, string error)
    {
        if (Context.Items["BroadcasterId"] is not Guid broadcasterId)
            return;
        string trimmed = error.Length > 2000 ? error[..2000] : error;
        await _widgetService.RecordRuntimeErrorAsync(broadcasterId.ToString(), widgetId, trimmed);
    }

    /// <summary>False when the connection's ticket names one widget and <paramref name="widgetId"/> is another.</summary>
    private bool MayActFor(string widgetId) =>
        Context.Items["WidgetId"] is not Guid scopedWidgetId
        || (
            Guid.TryParse(widgetId, out Guid requestedWidgetId)
            && requestedWidgetId == scopedWidgetId
        );

    private static WidgetActionResponse Refused(string error, string? code) =>
        new(false, null, error, code, new Dictionary<string, string>());
}
