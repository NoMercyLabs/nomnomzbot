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
    private static readonly TemplateHelperContext[] AllContexts =
    [
        TemplateHelperContext.Command,
        TemplateHelperContext.EventResponse,
        TemplateHelperContext.Timer,
    ];

    private static readonly TemplateHelperContext[] TriggerContexts =
    [
        TemplateHelperContext.Command,
        TemplateHelperContext.EventResponse,
    ];

    private static readonly TemplateHelperContext[] CommandOnly = [TemplateHelperContext.Command];

    public static IReadOnlyList<TemplateHelperEntry> All { get; } = BuildEntries();

    /// <summary>The full valid set for one context — the exact contract behind <c>GET /templates/helpers?context=</c>.</summary>
    public static IReadOnlyList<TemplateHelperEntry> ForContext(TemplateHelperContext context) =>
        [.. All.Where(e => e.Contexts.Contains(context))];

    private static TemplateHelperEntry Literal(
        string key,
        TemplateHelperContext[] contexts,
        string descriptionKey
    ) => new(key, contexts, new LocalizedText(descriptionKey));

    private static TemplateHelperEntry Prefixed(
        string displayKey,
        string prefix,
        TemplateHelperContext[] contexts,
        string descriptionKey
    ) => new(displayKey, contexts, new LocalizedText(descriptionKey), prefix);

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
            Prefixed("args.<n>", "args.", CommandOnly, "template.helper.args"),
            // ── Triggering user (command + event response — no bare trigger user on a timer) ──
            Literal("user", TriggerContexts, "template.helper.user"),
            Literal("user.name", TriggerContexts, "template.helper.user_name"),
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
        ];
}
