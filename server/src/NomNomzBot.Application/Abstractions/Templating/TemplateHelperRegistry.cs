// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;

namespace NomNomzBot.Application.Abstractions.Templating;

/// <summary>
/// The MACHINE-READABLE, single source of truth for every helper key <see cref="TemplateResolver"/>
/// can resolve (S042). Drives the <c>GET /api/v1/templates/helpers</c> endpoint and save-time
/// validation (<c>ITemplateHelperValidator</c>) — never maintained as a second, independent list:
/// <c>TemplateHelperCoverageTests</c> structurally enumerates both this registry and the resolver's
/// source and fails the build the moment they diverge.
/// </summary>
public static class TemplateHelperRegistry
{
    // Pipeline belongs in EVERY set below: a pipeline can be bound to a chat-command trigger, an
    // EventSub trigger or a timer, so it gets the union of what those surfaces can use (see
    // TemplateHelperContext.Pipeline). Omitting it here does not merely narrow the palette — it makes
    // save-time validation REJECT a valid `{user.name}` in a pipeline, which is worse than no
    // validation at all, because it blocks correct work rather than allowing incorrect work.
    private static readonly TemplateHelperContext[] AllContexts =
    [
        TemplateHelperContext.Command,
        TemplateHelperContext.EventResponse,
        TemplateHelperContext.Timer,
        TemplateHelperContext.Pipeline,
        TemplateHelperContext.Discord,
        TemplateHelperContext.Webhook,
    ];

    /// <summary>Discord-only seed aliases (discord.md §3.2/§3.4): supplied directly by the trigger
    /// handlers (<c>DiscordGoLiveNotificationHandler</c>, <c>SendDiscordNotificationAction</c>) as
    /// seed variables, never resolved by <see cref="TemplateResolver"/> itself.</summary>
    private static readonly TemplateHelperContext[] DiscordOnly = [TemplateHelperContext.Discord];

    /// <summary>Playlist-add (<c>!banger</c>) seed aliases: supplied directly by
    /// <c>PlaylistAddAction</c> for its own <c>message</c> field, never resolved by
    /// <see cref="TemplateResolver"/> itself — only meaningful on that action's templated field.</summary>
    private static readonly TemplateHelperContext[] PlaylistAddOnly =
    [
        TemplateHelperContext.Pipeline,
    ];

    private static readonly TemplateHelperContext[] TriggerContexts =
    [
        TemplateHelperContext.Command,
        TemplateHelperContext.EventResponse,
        TemplateHelperContext.Pipeline,
        TemplateHelperContext.Webhook,
    ];

    /// <summary>Helpers that need command arguments — a command trigger, or a pipeline bound to one.</summary>
    private static readonly TemplateHelperContext[] CommandArgContexts =
    [
        TemplateHelperContext.Command,
        TemplateHelperContext.Pipeline,
    ];

    /// <summary>Seeded directly by an EventSub/webhook-ingest handler's <c>BuildVariables</c> (ad breaks,
    /// moderation, subscriptions, cheers, raids, redemptions, reward lifecycle, stream lifecycle,
    /// engagement, supporter events) — every one of these fires only through
    /// <see cref="TemplateHelperContext.EventResponse"/> or a pipeline bound to that EventSub trigger,
    /// never a chat command, timer, Discord notification, or webhook body.</summary>
    private static readonly TemplateHelperContext[] EventSourceOnlyContexts =
    [
        TemplateHelperContext.EventResponse,
        TemplateHelperContext.Pipeline,
    ];

    /// <summary>Bare stream-identity keys ("broadcaster"/"title"/"game") shared verbatim by the Discord
    /// go-live seed aliases AND <c>ChannelOnlineHandler</c>/<c>ChannelOfflineHandler</c>'s
    /// <c>stream.online</c>/<c>stream.offline</c> triggers — same meaning (the channel now going live),
    /// so one entry per key covers both instead of two colliding literals.</summary>
    private static readonly TemplateHelperContext[] StreamLifecycleAndDiscordContexts =
    [
        TemplateHelperContext.Discord,
        TemplateHelperContext.EventResponse,
        TemplateHelperContext.Pipeline,
    ];

    public static IReadOnlyList<TemplateHelperEntry> All { get; } = BuildEntries();

    /// <summary>The full valid set for one context — the exact contract behind <c>GET /templates/helpers?context=</c>.</summary>
    public static IReadOnlyList<TemplateHelperEntry> ForContext(TemplateHelperContext context) =>
        [.. All.Where(e => e.Contexts.Contains(context))];

    /// <summary>
    /// The valid set for one context, narrowed to what a SPECIFIC EventSub-triggered event actually
    /// seeds (S-OWN16) — e.g. <c>context=eventResponse&amp;eventType=channel.raid</c> excludes
    /// subscription-only helpers like <c>tier</c>/<c>months</c> even though both are valid somewhere in
    /// the EventResponse context. Non-<see cref="TemplateHelperEntry.EventScoped"/> helpers (channel,
    /// time, user identity, etc.) are unaffected — they resolve for every event. <paramref name="eventType"/>
    /// must be a key from <see cref="EventResponsePresetCatalog.EventTypes"/>; an unknown key throws
    /// <see cref="ArgumentException"/> — callers validate against that list before calling (see
    /// <c>TemplatesController</c>).
    /// </summary>
    public static IReadOnlyList<TemplateHelperEntry> ForContext(
        TemplateHelperContext context,
        string eventType
    )
    {
        EventResponsePresetDto preset =
            EventResponsePresetCatalog.Presets.FirstOrDefault(p => p.EventType == eventType)
            ?? throw new ArgumentException($"Unknown event type '{eventType}'.", nameof(eventType));

        return
        [
            .. ForContext(context).Where(e => !e.EventScoped || preset.Variables.Any(e.Matches)),
        ];
    }

    private static TemplateHelperEntry Literal(
        string key,
        TemplateHelperContext[] contexts,
        string descriptionKey,
        bool eventScoped = false
    ) => new(key, contexts, new LocalizedText(descriptionKey), EventScoped: eventScoped);

    private static TemplateHelperEntry Prefixed(
        string displayKey,
        string prefix,
        TemplateHelperContext[] contexts,
        string descriptionKey,
        bool eventScoped = false
    ) => new(displayKey, contexts, new LocalizedText(descriptionKey), prefix, eventScoped);

    private static List<TemplateHelperEntry> BuildEntries() =>
        [
            // ── Channel / stream (all contexts) ─────────────────────────────
            Literal("channel", AllContexts, "template.helper.channel"),
            Literal("channel.display", AllContexts, "template.helper.channel_display"),
            Literal("channel.id", AllContexts, "template.helper.channel_id"),
            Literal("streamer", AllContexts, "template.helper.streamer"),
            Literal("stream.title", AllContexts, "template.helper.stream_title"),
            Literal("stream.game", AllContexts, "template.helper.stream_game"),
            Literal("stream.uptime", AllContexts, "template.helper.stream_uptime"),
            Literal("stream.viewers", AllContexts, "template.helper.stream_viewers"),
            Literal("stream.isLive", AllContexts, "template.helper.stream_is_live"),
            Literal("stream.startedAt", AllContexts, "template.helper.stream_started_at"),
            Literal("status", AllContexts, "template.helper.status"),
            Literal("tense", AllContexts, "template.helper.tense"),
            // ── Delivering platform (event response only; the event that fired the template) ──
            Literal("provider", [TemplateHelperContext.EventResponse], "template.helper.provider"),
            // ── Ad breaks (channel.ad_break.begin only) ─────────────────────
            Literal(
                "ad.duration",
                EventSourceOnlyContexts,
                "template.helper.ad_duration",
                eventScoped: true
            ),
            Literal(
                "ad.automatic",
                EventSourceOnlyContexts,
                "template.helper.ad_automatic",
                eventScoped: true
            ),
            // ── Follow (channel.follow) ──────────────────────────────────────
            Literal(
                "followed_at",
                EventSourceOnlyContexts,
                "template.helper.followed_at",
                eventScoped: true
            ),
            // ── Subscriptions / gifts / cheers (channel.subscribe, .subscription.message/.gift, .cheer) ──
            Literal("tier", EventSourceOnlyContexts, "template.helper.tier", eventScoped: true),
            Literal("months", EventSourceOnlyContexts, "template.helper.months", eventScoped: true),
            Literal("streak", EventSourceOnlyContexts, "template.helper.streak", eventScoped: true),
            Literal(
                "message",
                EventSourceOnlyContexts,
                "template.helper.message",
                eventScoped: true
            ),
            Literal(
                "also_said",
                EventSourceOnlyContexts,
                "template.helper.also_said",
                eventScoped: true
            ),
            Literal(
                "count",
                EventSourceOnlyContexts,
                "template.helper.event_count",
                eventScoped: true
            ),
            Literal(
                "anonymous",
                EventSourceOnlyContexts,
                "template.helper.anonymous",
                eventScoped: true
            ),
            Literal("bits", EventSourceOnlyContexts, "template.helper.bits", eventScoped: true),
            // ── Raids (channel.raid, channel.raid.out) ──────────────────────
            Literal(
                "viewers",
                EventSourceOnlyContexts,
                "template.helper.viewers",
                eventScoped: true
            ),
            // ── Reward redemptions + lifecycle (redemption.add, reward.paused/resumed/enabled/disabled) ──
            Literal("reward", EventSourceOnlyContexts, "template.helper.reward", eventScoped: true),
            Literal(
                "reward.id",
                EventSourceOnlyContexts,
                "template.helper.reward_id",
                eventScoped: true
            ),
            Literal(
                "redemption.id",
                EventSourceOnlyContexts,
                "template.helper.redemption_id",
                eventScoped: true
            ),
            Literal("cost", EventSourceOnlyContexts, "template.helper.cost", eventScoped: true),
            Literal("input", EventSourceOnlyContexts, "template.helper.input", eventScoped: true),
            // ── Moderation (channel.ban, channel.unban) + stream.offline (shares the "duration" key) ──
            Literal(
                "moderator",
                EventSourceOnlyContexts,
                "template.helper.moderator",
                eventScoped: true
            ),
            Literal("reason", EventSourceOnlyContexts, "template.helper.reason", eventScoped: true),
            Literal(
                "duration",
                EventSourceOnlyContexts,
                "template.helper.event_duration",
                eventScoped: true
            ),
            // ── Stream lifecycle (stream.online, stream.offline) ─────────────
            Literal(
                "broadcaster",
                StreamLifecycleAndDiscordContexts,
                "template.helper.broadcaster",
                eventScoped: true
            ),
            Literal(
                "title",
                StreamLifecycleAndDiscordContexts,
                "template.helper.title",
                eventScoped: true
            ),
            Literal(
                "game",
                StreamLifecycleAndDiscordContexts,
                "template.helper.game",
                eventScoped: true
            ),
            // ── Engagement triggers (engagement.first_time_chatter/.returning_chatter/.watch_streak/.session_first_message) ──
            Literal(
                "viewer.name",
                EventSourceOnlyContexts,
                "template.helper.viewer_name",
                eventScoped: true
            ),
            Literal(
                "engagement.daysSinceLastSeen",
                EventSourceOnlyContexts,
                "template.helper.engagement_days_since_last_seen",
                eventScoped: true
            ),
            Literal(
                "engagement.streak",
                EventSourceOnlyContexts,
                "template.helper.engagement_streak",
                eventScoped: true
            ),
            // ── Supporter events (supporter.tip/.membership/.merch/.charity/.any) ──
            Literal(
                "supporter.name",
                EventSourceOnlyContexts,
                "template.helper.supporter_name",
                eventScoped: true
            ),
            Literal(
                "supporter.kind",
                EventSourceOnlyContexts,
                "template.helper.supporter_kind",
                eventScoped: true
            ),
            Literal(
                "supporter.amount",
                EventSourceOnlyContexts,
                "template.helper.supporter_amount",
                eventScoped: true
            ),
            Literal(
                "supporter.currency",
                EventSourceOnlyContexts,
                "template.helper.supporter_currency",
                eventScoped: true
            ),
            Literal(
                "supporter.tier",
                EventSourceOnlyContexts,
                "template.helper.supporter_tier",
                eventScoped: true
            ),
            Literal(
                "supporter.quantity",
                EventSourceOnlyContexts,
                "template.helper.supporter_quantity",
                eventScoped: true
            ),
            Literal(
                "supporter.message",
                EventSourceOnlyContexts,
                "template.helper.supporter_message",
                eventScoped: true
            ),
            // ── OBS events (obs.<EventType> — obs-control.md §6): flat payload fields are dynamic per
            // event type, so this is a prefix family like custom.<name>.<field> ──
            Prefixed(
                "obs.event.<field>",
                "obs.event.",
                EventSourceOnlyContexts,
                "template.helper.obs_event_field",
                eventScoped: true
            ),
            // ── VTube Studio events (vts.<EventType> — vtube-studio.md §4): same dynamic-field shape ──
            Prefixed(
                "vts.event.<field>",
                "vts.event.",
                EventSourceOnlyContexts,
                "template.helper.vts_event_field",
                eventScoped: true
            ),
            // ── Time / date (all contexts) ──────────────────────────────────
            Literal("time", AllContexts, "template.helper.time"),
            Literal("time.utc", AllContexts, "template.helper.time_utc"),
            Literal("date", AllContexts, "template.helper.date"),
            // ── Bot identity (all contexts) ─────────────────────────────────
            Literal("botname", AllContexts, "template.helper.botname"),
            // ── Random helpers (all contexts) ───────────────────────────────
            Literal("random.user", AllContexts, "template.helper.random_user"),
            Prefixed(
                "random.number.<n>",
                "random.number.",
                AllContexts,
                "template.helper.random_number"
            ),
            Prefixed(
                "random.pick.<a.b.c>",
                "random.pick.",
                AllContexts,
                "template.helper.random_pick"
            ),
            // ── Named counters / pick-lists / custom-data / transforms (all contexts) ──
            Prefixed("count.<key>", "count.", AllContexts, "template.helper.count"),
            Prefixed("list.pick.<name>", "list.pick.", AllContexts, "template.helper.list_pick"),
            Prefixed(
                "custom.<name>.<field>",
                "custom.",
                AllContexts,
                "template.helper.custom_data"
            ),
            Prefixed(
                "transform.<name>:<text>",
                "transform.",
                AllContexts,
                "template.helper.transform"
            ),
            // ── Command arguments (command only) ────────────────────────────
            Prefixed("args.<n>", "args.", CommandArgContexts, "template.helper.args"),
            // ── Triggering user (command + event response — no bare trigger user on a timer) ──
            Literal("user", TriggerContexts, "template.helper.user"),
            Literal(
                "user.name",
                [.. TriggerContexts, TemplateHelperContext.Discord],
                "template.helper.user_name"
            ),
            Literal("user.id", TriggerContexts, "template.helper.user_id"),
            Literal("user.provider", TriggerContexts, "template.helper.user_provider"),
            Literal("user.accountAge", TriggerContexts, "template.helper.user_account_age"),
            Literal("user.followAge", TriggerContexts, "template.helper.user_follow_age"),
            Literal("user.pronouns", TriggerContexts, "template.helper.user_pronouns"),
            Literal("user.messageCount", TriggerContexts, "template.helper.user_message_count"),
            Literal("user.lastmessage", TriggerContexts, "template.helper.user_lastmessage"),
            Literal("user.link", TriggerContexts, "template.helper.user_link"),
            Literal("viewer.messages", TriggerContexts, "template.helper.viewer_messages"),
            Literal("viewer.watchtime", TriggerContexts, "template.helper.viewer_watchtime"),
            Literal("viewer.firstseen", TriggerContexts, "template.helper.viewer_firstseen"),
            Literal("viewer.redemptions", TriggerContexts, "template.helper.viewer_redemptions"),
            Literal("viewer.songrequests", TriggerContexts, "template.helper.viewer_songrequests"),
            Prefixed(
                "viewer.data.<key>",
                "viewer.data.",
                TriggerContexts,
                "template.helper.viewer_data"
            ),
            // ── @mention target (command + event response) ─────────────────
            Literal("target", TriggerContexts, "template.helper.target"),
            Literal("target.name", TriggerContexts, "template.helper.target_name"),
            Literal("target.id", TriggerContexts, "template.helper.target_id"),
            Literal("target.followAge", TriggerContexts, "template.helper.target_follow_age"),
            Literal("target.lastmessage", TriggerContexts, "template.helper.target_lastmessage"),
            Literal("target.link", TriggerContexts, "template.helper.target_link"),
            Literal("target.messages", TriggerContexts, "template.helper.target_messages"),
            Literal("target.watchtime", TriggerContexts, "template.helper.target_watchtime"),
            Literal("target.firstseen", TriggerContexts, "template.helper.target_firstseen"),
            Literal("target.redemptions", TriggerContexts, "template.helper.target_redemptions"),
            Literal("target.songrequests", TriggerContexts, "template.helper.target_songrequests"),
            Prefixed(
                "target.data.<key>",
                "target.data.",
                TriggerContexts,
                "template.helper.target_data"
            ),
            // ── Shared profile link (mirrors target when present, else user) ───
            Literal("link", TriggerContexts, "template.helper.link"),
            // ── Pronoun grammar (bare mirrors target when present, else user) ──
            Literal("subject", TriggerContexts, "template.helper.subject"),
            Literal("object", TriggerContexts, "template.helper.object"),
            Literal("possessive", TriggerContexts, "template.helper.possessive"),
            Literal("presentTense", TriggerContexts, "template.helper.present_tense"),
            Literal("genderedTerm", TriggerContexts, "template.helper.gendered_term"),
            Literal("user.subject", TriggerContexts, "template.helper.user_subject"),
            Literal("user.object", TriggerContexts, "template.helper.user_object"),
            Literal("user.possessive", TriggerContexts, "template.helper.user_possessive"),
            Literal("user.presentTense", TriggerContexts, "template.helper.user_present_tense"),
            Literal("user.genderedTerm", TriggerContexts, "template.helper.user_gendered_term"),
            Literal("user.tense", TriggerContexts, "template.helper.user_tense"),
            Literal("target.subject", TriggerContexts, "template.helper.target_subject"),
            Literal("target.object", TriggerContexts, "template.helper.target_object"),
            Literal("target.possessive", TriggerContexts, "template.helper.target_possessive"),
            Literal("target.presentTense", TriggerContexts, "template.helper.target_present_tense"),
            Literal("target.genderedTerm", TriggerContexts, "template.helper.target_gendered_term"),
            Literal("target.tense", TriggerContexts, "template.helper.target_tense"),
            Prefixed("verb:<sing|plur>", "verb:", TriggerContexts, "template.helper.verb"),
            Prefixed(
                "user.verb:<sing|plur>",
                "user.verb:",
                TriggerContexts,
                "template.helper.user_verb"
            ),
            Prefixed(
                "target.verb:<sing|plur>",
                "target.verb:",
                TriggerContexts,
                "template.helper.target_verb"
            ),
            // ── Discord notification seed aliases (Discord only — S-TWO-TEMPLATE-ENGINES) ──
            // "broadcaster"/"title"/"game" are registered above (StreamLifecycleAndDiscordContexts) —
            // shared verbatim with stream.online/.offline, not duplicated here.
            Literal("channel.name", DiscordOnly, "template.helper.channel_name"),
            Literal("channel.title", DiscordOnly, "template.helper.channel_title"),
            Literal("channel.game", DiscordOnly, "template.helper.channel_game"),
            Literal("raw.message", DiscordOnly, "template.helper.raw_message"),
            // ── Playlist-add (!banger) seed aliases — Pipeline only, S-OWN17 ──
            Literal("playlist_id", PlaylistAddOnly, "template.helper.playlist_id"),
            Literal("track_name", PlaylistAddOnly, "template.helper.track_name"),
        ];
}
