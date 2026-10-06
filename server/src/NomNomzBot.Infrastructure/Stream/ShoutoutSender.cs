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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Entities;
using Channel = NomNomzBot.Domain.Identity.Entities.Channel;

namespace NomNomzBot.Infrastructure.Stream;

/// <summary>
/// The one place a shoutout is sent: the native Helix call (unless the request skips it), the templated chat
/// announcement and the optional TTS. The pipeline action calls it for an immediate run and the queue worker
/// calls it for a run that waited out Twitch's global cooldown. Both cooldown stamps are set after every run,
/// including one whose native call was skipped (old-bot parity).
/// </summary>
public sealed class ShoutoutSender : IShoutoutSender
{
    private readonly ITwitchChatApi _chat;
    private readonly IChannelRegistry _registry;
    private readonly IApplicationDbContext _db;
    private readonly ITtsDispatchService _tts;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ShoutoutSender> _logger;

    public ShoutoutSender(
        ITwitchChatApi chat,
        IChannelRegistry registry,
        IApplicationDbContext db,
        ITtsDispatchService tts,
        TimeProvider timeProvider,
        ILogger<ShoutoutSender> logger
    )
    {
        _chat = chat;
        _registry = registry;
        _db = db;
        _tts = tts;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ShoutoutTemplateSelection> SelectTemplateAsync(
        Guid broadcasterId,
        TwitchUser target,
        string templateOverride,
        CancellationToken cancellationToken
    )
    {
        Channel? channel = await _db
            .Channels.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == broadcasterId, cancellationToken);

        // Old-bot parity: the announcement template is the TARGET's own — each NomNomzBot streamer sets how
        // they want to be announced when shouted out BY ANYONE, not a template the shouting streamer picks
        // for them (ShoutoutQueueService.ExecuteShoutoutAsync read channel?.ShoutoutTemplate off the target's
        // own Channel row). Only a target who is themselves a NomNomzBot streamer has one; a plain Twitch
        // channel with no account here falls through to the shouting streamer's own default.
        Channel? targetChannel = await _db
            .Channels.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TwitchChannelId == target.Id, cancellationToken);

        // The shouting streamer's OWN per-target note (old-bot parity: the legacy bot's Shoutout table,
        // keyed by (channel, shouted user)) — a deliberate personal line written for THIS specific person,
        // regardless of whether they've ever connected to NomNomzBot. Wins over the target's own
        // self-managed template: it is this broadcaster's own choice about how they introduce this
        // specific person, not something another streamer's account setting should override.
        ShoutoutOverride? perTargetOverride = await _db
            .ShoutoutOverrides.AsNoTracking()
            .FirstOrDefaultAsync(
                o => o.BroadcasterId == broadcasterId && o.TargetTwitchUserId == target.Id,
                cancellationToken
            );

        string? template =
            !string.IsNullOrWhiteSpace(templateOverride) ? templateOverride
            : !string.IsNullOrWhiteSpace(perTargetOverride?.MessageTemplate)
                ? perTargetOverride.MessageTemplate
            : !string.IsNullOrWhiteSpace(targetChannel?.ShoutoutTemplate)
                ? targetChannel.ShoutoutTemplate
            : !string.IsNullOrWhiteSpace(channel?.ShoutoutTemplate) ? channel.ShoutoutTemplate
            : null;
        return new(template, PersonalityTone.Normalize(channel?.Personality));
    }

    public async Task<ActionResult> SendAsync(
        ShoutoutRequest request,
        CancellationToken cancellationToken
    )
    {
        TwitchUser target = request.Target;
        string rawUserId = target.Id;

        // rawUserId is the Twitch id of the channel to shout out. The sub-client resolves this channel's
        // tenant Guid → Twitch id internally and sends the shoutout as its own moderator.
        Result result = request.SkipNativeCall
            ? Result.Failure("native shoutout skipped: cooldown active")
            : await _chat.SendShoutoutAsync(request.BroadcasterId, rawUserId, cancellationToken);

        ChannelContext? channelCtx = _registry.Get(request.BroadcasterId);
        if (channelCtx is not null)
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
            .FirstOrDefaultAsync(c => c.Id == request.BroadcasterId, cancellationToken);

        Result announceResult = await _chat.SendAnnouncementAsync(
            request.BroadcasterId,
            request.Announcement,
            color: null,
            cancellationToken
        );
        if (announceResult.IsFailure)
            _logger.LogWarning(
                "Shoutout announcement failed for {UserId}: {Error}",
                rawUserId,
                announceResult.ErrorMessage
            );

        // TTS is opt-in per invocation (old-bot parity: manual !so speaks it, an automated
        // presence-detection shoutout stays silent by simply never passing tts:true) and best-effort — a
        // synthesis/dispatch failure never fails the shoutout itself. Speaks in the SHOUTED-OUT target's
        // own assigned voice (ResolveVoiceAsync looks up UserTtsVoices by RequestedByTwitchUserId) — old-bot
        // parity (ShoutoutQueueService.ExecuteShoutoutAsync called SendCachedTts(ttsText, TargetUserId, ...)).
        // A raid speaks by default (old-bot parity); an explicit tts:false still wins.
        if (request.Speak && channel is not null)
        {
            Result<TtsDispatchOutcome> speakResult = await _tts.RequestSpeakAsync(
                new(
                    BroadcasterId: request.BroadcasterId,
                    RequestedByUserId: channel.OwnerUserId,
                    RequestedByTwitchUserId: target.Id,
                    RequestedByDisplayName: target.DisplayName,
                    Text: request.Announcement,
                    VoiceIdOverride: null,
                    BitsAmount: 0,
                    CommunityStanding: "broadcaster",
                    SourceMessageId: null,
                    StreamId: null
                ),
                cancellationToken
            );
            if (speakResult.IsFailure)
                _logger.LogWarning(
                    "Shoutout TTS failed for {UserId}: {Error}",
                    rawUserId,
                    speakResult.ErrorMessage
                );
        }

        // Truthful outcome: only a failed ANNOUNCEMENT is a failed shoutout. The NATIVE Helix shoutout is
        // best-effort, never the verdict: Twitch enforces its own cooldowns and answers 429 for a perfectly
        // normal second `!so` while the announcement the viewer actually sees has already posted fine
        // (live, 2026-08-25).
        if (result.IsFailure)
            _logger.LogDebug(
                "Native Twitch shoutout for {UserId} did not go through ({Error}) — the announcement carries it.",
                rawUserId,
                result.ErrorMessage
            );
        if (announceResult.IsFailure)
            return ActionResult.Failure(
                $"shoutout sent to {rawUserId} but the announcement failed: {announceResult.ErrorMessage}"
            );
        return ActionResult.Success($"shoutout sent to {rawUserId}");
    }

    /// <summary>Resolves a whole-value <c>{key}</c> reference against the pipeline's variable bag; a value
    /// that isn't wholly wrapped in braces passes through unchanged.</summary>
    internal static string ResolveVariable(
        string value,
        IReadOnlyDictionary<string, string> variables
    )
    {
        if (!value.StartsWith('{') || !value.EndsWith('}'))
            return value;
        variables.TryGetValue(value[1..^1], out string? resolved);
        return resolved ?? string.Empty;
    }
}
