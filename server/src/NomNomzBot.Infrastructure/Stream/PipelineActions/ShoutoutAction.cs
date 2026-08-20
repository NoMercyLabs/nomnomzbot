// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;
using Channel = NomNomzBot.Domain.Identity.Entities.Channel;

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
///         (default: false — silent). Set true on a manual/chat-triggered shoutout; leave false/omitted on
///         an automated one.
///
/// The announcement template supports the full 90+ variable set (commands-pipelines.md §6.3), seeded with
/// {target}/{target.name}/{target.link} resolved from the shoutout's own target (not the DB {target.*}
/// lookup, since a shouted-out channel is rarely a known viewer). No template configured on the channel
/// falls back to "Go check out {target.name} — {target.link}".
///
/// Usage example:
///   { "type": "shoutout", "user_id": "{user.id}", "cooldown_minutes": 60, "tts": true }
/// </summary>
public sealed class ShoutoutAction : ICommandAction
{
    private const string DefaultTemplate = "Go check out {target.name} — {target.link}";

    private static readonly TimeSpan DefaultPerUserCooldown = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan DefaultGlobalCooldown = TimeSpan.FromMinutes(2);

    private readonly ITwitchChatApi _chat;
    private readonly ITwitchUsersApi _users;
    private readonly IChannelRegistry _registry;
    private readonly IApplicationDbContext _db;
    private readonly ITemplateResolver _templateResolver;
    private readonly ITtsDispatchService _tts;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ShoutoutAction> _logger;

    public string ActionType => "shoutout";

    public ShoutoutAction(
        ITwitchChatApi chat,
        ITwitchUsersApi users,
        IChannelRegistry registry,
        IApplicationDbContext db,
        ITemplateResolver templateResolver,
        ITtsDispatchService tts,
        TimeProvider timeProvider,
        ILogger<ShoutoutAction> logger
    )
    {
        _chat = chat;
        _users = users;
        _registry = registry;
        _db = db;
        _templateResolver = templateResolver;
        _tts = tts;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? rawUserId = action.GetString("user_id") ?? string.Empty;

        // Resolve {variable} references inside the user_id param
        if (rawUserId.StartsWith('{') && rawUserId.EndsWith('}'))
        {
            string key = rawUserId[1..^1];
            ctx.Variables.TryGetValue(key, out rawUserId!);
        }

        rawUserId = rawUserId?.Trim().TrimStart('@') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawUserId))
            return ActionResult.Failure("shoutout action requires a non-empty 'user_id'");

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
        rawUserId = target.Id;

        int perUserMinutes = action.GetInt("cooldown_minutes", 60);
        int globalMinutes = action.GetInt("global_cooldown_minutes", 2);
        TimeSpan perUserCooldown = TimeSpan.FromMinutes(perUserMinutes);
        TimeSpan globalCooldown = TimeSpan.FromMinutes(globalMinutes > 0 ? globalMinutes : 2);

        // Check cooldowns via ChannelContext
        ChannelContext? channelCtx = _registry.Get(ctx.BroadcasterId);
        if (channelCtx is not null)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();

            // Global cooldown
            if (
                channelCtx.LastGlobalShoutout.HasValue
                && now - channelCtx.LastGlobalShoutout.Value < globalCooldown
            )
            {
                _logger.LogDebug(
                    "Shoutout to {UserId} skipped — global cooldown active",
                    rawUserId
                );
                return ActionResult.Success("skipped (global cooldown)");
            }

            // Per-user cooldown
            if (
                channelCtx.LastShoutoutPerUser.TryGetValue(rawUserId, out DateTimeOffset lastSo)
                && now - lastSo < perUserCooldown
            )
            {
                _logger.LogDebug(
                    "Shoutout to {UserId} skipped — per-user cooldown active",
                    rawUserId
                );
                return ActionResult.Success("skipped (per-user cooldown)");
            }
        }

        // rawUserId is the Twitch id of the channel to shout out. The sub-client resolves this channel's
        // tenant Guid → Twitch id internally and sends the shoutout as its own moderator.
        Result result = await _chat.SendShoutoutAsync(
            ctx.BroadcasterId,
            rawUserId,
            ctx.CancellationToken
        );
        bool success = result.IsSuccess;

        if (success && channelCtx is not null)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            channelCtx.LastGlobalShoutout = now;
            channelCtx.LastShoutoutPerUser[rawUserId] = now;
        }

        // The native Helix shoutout carries no visible text and Twitch renders it minimally — post the
        // channel's own custom-templated announcement too (old-bot parity), independent of whether the
        // native call itself succeeded (a cooldown-throttled native shoutout on Twitch's side should not
        // silently swallow the announcement the streamer configured).
        Channel? channel = await _db
            .Channels.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == ctx.BroadcasterId, ctx.CancellationToken);
        string template =
            channel is null || string.IsNullOrWhiteSpace(channel.ShoutoutTemplate)
                ? DefaultTemplate
                : channel.ShoutoutTemplate;

        Dictionary<string, string> seed = new(ctx.Variables, StringComparer.OrdinalIgnoreCase)
        {
            ["target"] = target.Login,
            ["target.id"] = target.Id,
            ["target.name"] = target.DisplayName,
            ["target.link"] = $"twitch.tv/{target.Login}",
        };
        string announcement = await _templateResolver.ResolveAsync(
            template,
            seed,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );
        Result announceResult = await _chat.SendAnnouncementAsync(
            ctx.BroadcasterId,
            announcement,
            color: null,
            ctx.CancellationToken
        );
        if (announceResult.IsFailure)
            _logger.LogWarning(
                "Shoutout announcement failed for {UserId}: {Error}",
                rawUserId,
                announceResult.ErrorMessage
            );

        // TTS is opt-in per invocation (old-bot parity: manual !so speaks it, an automated
        // presence-detection shoutout stays silent by simply never passing tts:true) and best-effort — a
        // synthesis/dispatch failure never fails the shoutout itself.
        if (action.GetBool("tts", false) && channel is not null)
        {
            Result<TtsDispatchOutcome> speakResult = await _tts.RequestSpeakAsync(
                new TtsSpeakRequest(
                    BroadcasterId: ctx.BroadcasterId,
                    RequestedByUserId: channel.OwnerUserId,
                    RequestedByTwitchUserId: channel.TwitchChannelId ?? string.Empty,
                    RequestedByDisplayName: channel.Name,
                    Text: announcement,
                    VoiceIdOverride: null,
                    BitsAmount: 0,
                    CommunityStanding: "broadcaster",
                    SourceMessageId: null,
                    StreamId: null
                ),
                ctx.CancellationToken
            );
            if (speakResult.IsFailure)
                _logger.LogWarning(
                    "Shoutout TTS failed for {UserId}: {Error}",
                    rawUserId,
                    speakResult.ErrorMessage
                );
        }

        return success
            ? ActionResult.Success($"shoutout sent to {rawUserId}")
            : ActionResult.Failure($"Twitch shoutout API failed for {rawUserId}");
    }
}
