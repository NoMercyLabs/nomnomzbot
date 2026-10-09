// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Stream.PipelineActions;

/// <summary>
/// Pipeline action that shouts a channel out: the native Twitch Helix shoutout, PLUS a templated chat
/// announcement (the channel's <see cref="Channel.ShoutoutTemplate"/>, falling back to a sensible default),
/// PLUS optional TTS — the parity gap with hand-rolled bots that do more than the bare Helix call. Old-bot
/// parity: a manual (chat-triggered) shoutout reads the announcement aloud; an automated one (e.g. a
/// presence-detection event response) stays silent by simply never passing <c>tts:true</c>.
///
/// Parameters:
///   user_id  — Twitch user ID **or login/channel name** to shout out (required; a leading @ is
///              tolerated, a login is resolved to its id via Helix Get Users). Supports variable
///              substitution — e.g. "{timer.message}" for a rotating auto-shoutout list.
///   cooldown_minutes — Per-user cooldown in minutes (default: 60).
///   global_cooldown_minutes — Global shoutout cooldown in minutes (default: 2).
///   tts — When true, also reads the resolved announcement aloud via the channel's configured TTS pipeline
///         (default: false — silent, except on a raid, where it defaults to true). Set true on a
///         manual/chat-triggered shoutout; leave false/omitted on an automated one; false always wins.
///         A shoutout inside either cooldown is queued in full, including its announcement and TTS.
///   priority — When true, the shoutout waits in the queue with raid priority. Default false.
///   template — Per-invocation template override (e.g. a value drawn from a pick_from_list step for a
///              varied/snarky rotation). Takes priority over the channel's stored ShoutoutTemplate, which
///              in turn takes priority over the built-in default.
///
/// The announcement template supports the full 90+ variable set (commands-pipelines.md §6.3), seeded with
/// {target}/{target.name}/{target.link} resolved from the shoutout's own target (not the DB {target.*}
/// lookup, since a shouted-out channel is rarely a known viewer). With no custom template at any level, the
/// line is composed from the shouting channel's tone (<c>shoutout</c>/<c>announcement</c> in the tone
/// catalogue: the old bot's snarky pool for a sassy channel, plain lines for an informative one) and any
/// channel or platform override of that slot.
///
/// Usage example (static template):
///   { "type": "shoutout", "user_id": "{user.id}", "cooldown_minutes": 60, "tts": true }
/// Usage example (varied pool — pair with a preceding pick_from_list step writing into {line}):
///   { "type": "shoutout", "user_id": "{args.1}", "template": "{line}", "tts": true }
/// </summary>
public sealed class ShoutoutAction : ICommandAction
{
    private const string RaidEventName = "channel.raid";
    private const string FallbackGame = "something awesome";
    private const string DefaultTemplate =
        "Go check out {target.name}! Follow the channel to catch the next stream.";

    // The old bot's failure texts. A graph sends the failure to chat through {last.error}, so the text is
    // what a viewer reads. Fixed, not a tone slot: a failure has no target, so no personality is resolved.
    private const string MissingUserText = "You need to specify a user to shoutout!";
    private const string UnexpectedErrorText = "An error occurred while processing the shoutout.";

    private static readonly TimeSpan DefaultPerUserCooldown = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan DefaultGlobalCooldown = TimeSpan.FromMinutes(2);

    private readonly ITwitchUsersApi _users;
    private readonly ITwitchChannelsApi _channels;
    private readonly ITwitchStreamsApi _streams;
    private readonly IChannelRegistry _registry;
    private readonly IShoutoutQueue _queue;
    private readonly IShoutoutSender _sender;
    private readonly ITemplateResolver _templateResolver;
    private readonly IBuiltinResponseComposer _composer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ShoutoutAction> _logger;

    public string ActionType => "shoutout";

    public LocalizedText Category => new("pipeline.category.stream");

    public LocalizedText Description => new("pipeline.shoutout.description");
    public bool ResolvesOwnTemplates => true;

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "user_id",
                PipelineActionFieldKind.TwitchUser,
                Required: true,
                Description: new("pipeline.shoutout.user_id.help")
            ),
            new(
                "cooldown_minutes",
                PipelineActionFieldKind.Number,
                Description: new("pipeline.shoutout.cooldown_minutes.help")
            ),
            new(
                "global_cooldown_minutes",
                PipelineActionFieldKind.Number,
                Description: new("pipeline.shoutout.global_cooldown_minutes.help")
            ),
            new(
                "tts",
                PipelineActionFieldKind.Boolean,
                Description: new("pipeline.shoutout.tts.help")
            ),
            new(
                "priority",
                PipelineActionFieldKind.Boolean,
                Description: new("pipeline.shoutout.priority.help")
            ),
            new(
                "template",
                PipelineActionFieldKind.Text,
                Templated: true,
                Description: new("pipeline.shoutout.template.help")
            ),
        ];

    public ShoutoutAction(
        ITwitchUsersApi users,
        ITwitchChannelsApi channels,
        ITwitchStreamsApi streams,
        IChannelRegistry registry,
        IShoutoutQueue queue,
        IShoutoutSender sender,
        ITemplateResolver templateResolver,
        IBuiltinResponseComposer composer,
        TimeProvider timeProvider,
        ILogger<ShoutoutAction> logger
    )
    {
        _users = users;
        _channels = channels;
        _streams = streams;
        _registry = registry;
        _queue = queue;
        _sender = sender;
        _templateResolver = templateResolver;
        _composer = composer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        try
        {
            return await RunAsync(ctx, action);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Shoutout action failed unexpectedly");
            return ActionResult.Failure(UnexpectedErrorText);
        }
    }

    private async Task<ActionResult> RunAsync(PipelineExecutionContext ctx, ActionDefinition action)
    {
        string rawUserId = ShoutoutSender.ResolveVariable(
            action.GetString("user_id") ?? string.Empty,
            ctx.Variables
        );
        rawUserId = MentionParser.ParseUserMention(rawUserId);
        if (string.IsNullOrWhiteSpace(rawUserId))
            return ActionResult.Failure(MissingUserText);

        // A curated shoutout list holds channel NAMES; Helix wants the numeric id — resolve a login. Either
        // way, resolve the full user record here (not just the id) — the announcement template needs the
        // target's login/display name, and a numeric-id input never had them.
        TwitchUser? target;
        if (rawUserId.All(char.IsAsciiDigit))
        {
            Result<IReadOnlyList<TwitchUser>> lookup = await _users.GetUsersByIdsAsync(
                [rawUserId],
                ctx.CancellationToken
            );
            target = lookup.IsSuccess ? lookup.Value.FirstOrDefault() : null;
        }
        else
        {
            Result<IReadOnlyList<TwitchUser>> lookup = await _users.GetUsersByLoginsAsync(
                [rawUserId.ToLowerInvariant()],
                ctx.CancellationToken
            );
            target = lookup.IsSuccess ? lookup.Value.FirstOrDefault() : null;
        }
        if (target is null)
            return ActionResult.Failure($"shoutout target '{rawUserId}' was not found on Twitch");

        int perUserMinutes = action.GetInt("cooldown_minutes", 60);
        int globalMinutes = action.GetInt("global_cooldown_minutes", 2);
        TimeSpan perUserCooldown = TimeSpan.FromMinutes(perUserMinutes);
        TimeSpan globalCooldown = TimeSpan.FromMinutes(globalMinutes > 0 ? globalMinutes : 2);

        // Every trigger waits in the same queue while either Twitch cooldown is active. A raid or a step
        // marked priority runs ahead of standard entries after the current shoutout finishes.
        bool isRaid =
            (
                ctx.Variables.TryGetValue("event.name", out string? eventName)
                && string.Equals(eventName, RaidEventName, StringComparison.OrdinalIgnoreCase)
            ) || action.GetBool("priority");

        // A request can arrive before the worker's first pass after a restart. Load older pending items
        // before deciding whether this request may run immediately.
        await _queue.RecoverAsync(ctx.CancellationToken);

        ChannelContext? channelCtx = _registry.Get(ctx.BroadcasterId);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        bool perUserActive = ShoutoutCooldowns.PerUserActive(
            channelCtx,
            target.Id,
            perUserCooldown,
            now
        );

        if (perUserActive && !isRaid && !string.IsNullOrWhiteSpace(ctx.MessageId))
        {
            if (channelCtx is not null)
            {
                lock (channelCtx.LastManualShoutoutAnnouncementPerUser)
                {
                    if (
                        channelCtx.LastManualShoutoutAnnouncementPerUser.TryGetValue(
                            target.Id,
                            out DateTimeOffset lastAnnouncement
                        )
                        && now - lastAnnouncement < DefaultGlobalCooldown
                    )
                        return ActionResult.Success("already announced (per-user cooldown)");
                    channelCtx.LastManualShoutoutAnnouncementPerUser[target.Id] = now;
                }
            }

            string announcement = await ComposeAnnouncementAsync(ctx, action, target);
            ActionResult announced = await _sender.SendAsync(
                new(ctx.BroadcasterId, target, announcement, Speak: true, SkipNative: true),
                ctx.CancellationToken
            );
            if (!announced.Succeeded && channelCtx is not null)
                channelCtx.LastManualShoutoutAnnouncementPerUser.TryRemove(target.Id, out _);
            return announced;
        }

        // A waiting queue also holds back a run that is already past the cooldown, so the order stays fair.
        if (
            ShoutoutCooldowns.GlobalActive(channelCtx, globalCooldown, now)
            || perUserActive
            || _queue.Peek(ctx.BroadcasterId) is not null
        )
        {
            string announcement = await ComposeAnnouncementAsync(ctx, action, target);
            bool added = await _queue.EnqueueAsync(
                new QueuedShoutout(
                    ctx.BroadcasterId,
                    target,
                    ctx.TriggeredByUserId,
                    isRaid,
                    globalCooldown,
                    perUserCooldown,
                    now,
                    Announcement: announcement,
                    Speak: action.GetBool("tts", isRaid)
                ),
                ctx.CancellationToken
            );
            return ActionResult.Success(added ? "queued (cooldown)" : "already queued (cooldown)");
        }

        string immediateAnnouncement = await ComposeAnnouncementAsync(ctx, action, target);
        ActionResult sent = await _sender.SendAsync(
            new(ctx.BroadcasterId, target, immediateAnnouncement, action.GetBool("tts", isRaid)),
            ctx.CancellationToken
        );
        if (!sent.Retryable)
            return sent;

        bool queued = await _queue.EnqueueAsync(
            new QueuedShoutout(
                ctx.BroadcasterId,
                target,
                ctx.TriggeredByUserId,
                isRaid,
                globalCooldown,
                perUserCooldown,
                now,
                Announcement: immediateAnnouncement,
                Speak: action.GetBool("tts", isRaid)
            ),
            ctx.CancellationToken
        );
        return ActionResult.Success(queued ? "queued (rate limit)" : "already queued (rate limit)");
    }

    private readonly record struct TargetState(string Game, string Title, bool IsLive);

    /// <summary>
    /// The target's own game, title and live state. A live target answers from one streams lookup; an offline
    /// one (or a failed lookup) falls back to its channel information. No category gives the old bot's
    /// "something awesome".
    /// </summary>
    private async Task<TargetState> ResolveTargetStateAsync(TwitchUser target, CancellationToken ct)
    {
        Result<TwitchPage<TwitchStream>> streams = await _streams.GetStreamsAsync(
            new(UserIds: [target.Id]),
            new(PageSize: 1),
            ct
        );
        TwitchStream? live = streams.IsSuccess
            ? streams.Value.Items.FirstOrDefault(s => s.UserId == target.Id)
            : null;
        if (live is not null)
            return new(
                string.IsNullOrWhiteSpace(live.GameName) ? FallbackGame : live.GameName,
                live.Title,
                true
            );

        Result<IReadOnlyList<TwitchChannelInformation>> lookup =
            await _channels.GetChannelInformationByTwitchIdsAsync([target.Id], ct);
        TwitchChannelInformation? info = lookup.IsSuccess
            ? lookup.Value.FirstOrDefault(c => c.BroadcasterId == target.Id)
            : null;
        return new(
            string.IsNullOrWhiteSpace(info?.GameName) ? FallbackGame : info.GameName,
            info?.Title ?? string.Empty,
            false
        );
    }

    private async Task<string> ComposeAnnouncementAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action,
        TwitchUser target
    )
    {
        string templateOverride = ShoutoutSender.ResolveVariable(
            action.GetString("template") ?? string.Empty,
            ctx.Variables
        );
        ShoutoutTemplateSelection selection = await _sender.SelectTemplateAsync(
            ctx.BroadcasterId,
            target,
            templateOverride,
            ctx.CancellationToken
        );
        TargetState state = await ResolveTargetStateAsync(target, ctx.CancellationToken);
        Dictionary<string, string> seed = new(ctx.Variables, StringComparer.OrdinalIgnoreCase)
        {
            ["target"] = target.Login,
            ["target.id"] = target.Id,
            ["target.name"] = target.DisplayName,
            ["target.link"] = $"twitch.tv/{target.Login}",
            ["target.game"] = state.Game,
            ["target.title"] = state.Title,
            ["target.isLive"] = state.IsLive ? "true" : "false",
            ["game"] = state.Game,
            ["title"] = state.Title,
            ["status"] = state.IsLive ? "live" : "offline",
        };
        if (selection.Template is null)
            return await _composer.ComposeAsync(
                new()
                {
                    BroadcasterId = ctx.BroadcasterId,
                    Personality = selection.Personality,
                    BuiltinKey = BuiltinResponseSlots.Shoutout.Key,
                    Slot = BuiltinResponseSlots.Shoutout.Announcement,
                    NeutralFallback = DefaultTemplate,
                    Variables = seed,
                },
                ctx.CancellationToken
            );
        return await _templateResolver.ResolveAsync(
            selection.Template,
            seed,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );
    }
}
