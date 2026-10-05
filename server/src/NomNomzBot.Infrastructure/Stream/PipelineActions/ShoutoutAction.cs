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
///         A manual or raid shoutout inside the global cooldown is queued; inside only the per-user cooldown
///         it announces and speaks at once with the native Helix call skipped. Any other run inside a
///         cooldown is skipped.
///   template — Per-invocation template override (e.g. a value drawn from a pick_from_list step for a
///              varied/snarky rotation). Takes priority over the channel's stored ShoutoutTemplate, which
///              in turn takes priority over the built-in default.
///
/// The announcement template supports the full 90+ variable set (commands-pipelines.md §6.3), seeded with
/// {target}/{target.name}/{target.link} resolved from the shoutout's own target (not the DB {target.*}
/// lookup, since a shouted-out channel is rarely a known viewer). With no custom template at any level, the
/// line is composed from the shouting channel's tone (<c>shoutout</c>/<c>announcement</c> in the tone
/// catalogue: the old bot's snarky pool for a sassy channel, "Go check out {target.name} — {target.link}"
/// for an informative one) and any channel or platform override of that slot.
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
    private const string DefaultTemplate = "Go check out {target.name} — {target.link}";

    // The old bot's failure texts. A graph sends the failure to chat through {last.error}, so the text is
    // what a viewer reads. Fixed, not a tone slot: a failure has no target, so no personality is resolved.
    private const string MissingUserText = "You need to specify a user to shoutout!";
    private const string UnexpectedErrorText = "An error occurred while processing the shoutout.";

    private static readonly TimeSpan DefaultPerUserCooldown = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan DefaultGlobalCooldown = TimeSpan.FromMinutes(2);

    private readonly ITwitchUsersApi _users;
    private readonly ITwitchChannelsApi _channels;
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
                "template",
                PipelineActionFieldKind.Text,
                Templated: true,
                Description: new("pipeline.shoutout.template.help")
            ),
        ];

    public ShoutoutAction(
        ITwitchUsersApi users,
        ITwitchChannelsApi channels,
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

        // Old-bot parity (ShoutoutQueueService): a raid or a manual (chat-triggered) shoutout inside Twitch's
        // global cooldown WAITS in the queue and then runs in full; inside only the per-user cooldown it runs
        // at once with the native Helix call skipped. Every other run (timer, event response, owner action)
        // is skipped whole. A manual run is one a chat message started, so it carries a message id.
        bool isRaid =
            ctx.Variables.TryGetValue("event.name", out string? eventName)
            && string.Equals(eventName, RaidEventName, StringComparison.OrdinalIgnoreCase);
        bool waits = isRaid || !string.IsNullOrEmpty(ctx.MessageId);

        ChannelContext? channelCtx = _registry.Get(ctx.BroadcasterId);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        bool perUserActive = ShoutoutCooldowns.PerUserActive(
            channelCtx,
            target.Id,
            perUserCooldown,
            now
        );

        // A waiting queue also holds back a run that is already past the cooldown, so the order stays fair.
        if (
            ShoutoutCooldowns.GlobalActive(channelCtx, globalCooldown, now)
            || _queue.Peek(ctx.BroadcasterId) is not null
        )
        {
            if (!waits)
            {
                _logger.LogDebug(
                    "Shoutout to {UserId} skipped — global cooldown active",
                    target.Id
                );
                return ActionResult.Success("skipped (global cooldown)");
            }

            bool added = _queue.Enqueue(
                new QueuedShoutout(
                    ctx.BroadcasterId,
                    target,
                    await ComposeAnnouncementAsync(ctx, action, target),
                    action.GetBool("tts", isRaid),
                    ctx.TriggeredByUserId,
                    isRaid,
                    globalCooldown,
                    perUserCooldown,
                    now
                )
            );
            return ActionResult.Success(
                added ? "queued (global cooldown)" : "already queued (global cooldown)"
            );
        }

        if (perUserActive && !waits)
        {
            _logger.LogDebug("Shoutout to {UserId} skipped — per-user cooldown active", target.Id);
            return ActionResult.Success("skipped (per-user cooldown)");
        }

        return await _sender.SendAsync(
            new(
                ctx.BroadcasterId,
                target,
                await ComposeAnnouncementAsync(ctx, action, target),
                action.GetBool("tts", isRaid),
                perUserActive
            ),
            ctx.CancellationToken
        );
    }

    /// <summary>The target's current Twitch category, or the old bot's "something awesome" when there is none.</summary>
    private async Task<string> ResolveGameAsync(TwitchUser target, CancellationToken ct)
    {
        Result<IReadOnlyList<TwitchChannelInformation>> lookup =
            await _channels.GetChannelInformationByTwitchIdsAsync([target.Id], ct);
        string? game = lookup.IsSuccess
            ? lookup.Value.FirstOrDefault(c => c.BroadcasterId == target.Id)?.GameName
            : null;
        return string.IsNullOrWhiteSpace(game) ? FallbackGame : game;
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
        Dictionary<string, string> seed = new(ctx.Variables, StringComparer.OrdinalIgnoreCase)
        {
            ["target"] = target.Login,
            ["target.id"] = target.Id,
            ["target.name"] = target.DisplayName,
            ["target.link"] = $"twitch.tv/{target.Login}",
            ["target.game"] = await ResolveGameAsync(target, ctx.CancellationToken),
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
