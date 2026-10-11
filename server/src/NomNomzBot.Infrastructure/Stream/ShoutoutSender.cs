// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Entities;
using Channel = NomNomzBot.Domain.Identity.Entities.Channel;

namespace NomNomzBot.Infrastructure.Stream;

/// <summary>
/// The one place a shoutout is sent: the native Helix call (unless the request skips it), the templated chat
/// announcement and the optional TTS. The pipeline action calls it for an immediate run and the queue worker
/// calls it for a run that waited out Twitch's cooldowns. The announcement waits for a successful native call.
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
        bool replayUncertain = false;
        string? attemptId = null;

        if (!request.SkipNative && request.DeliveryKey is not null)
        {
            attemptId = AttemptId(request.DeliveryKey);
            if (await _db.ChannelEvents.AnyAsync(e => e.Id == attemptId, cancellationToken))
                replayUncertain = true;
            else
            {
                _db.ChannelEvents.Add(
                    new ChannelEvent
                    {
                        Id = attemptId,
                        ChannelId = request.BroadcasterId,
                        Type = "bot.shoutout.native-attempt",
                        Data = JsonSerializer.Serialize(new { toUserId = rawUserId }),
                        CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
                    }
                );
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        // rawUserId is the Twitch id of the channel to shout out. The sub-client resolves this channel's
        // tenant Guid → Twitch id internally and sends the shoutout as its own moderator.
        if (!request.SkipNative && !replayUncertain)
        {
            Result result = await _chat.SendShoutoutAsync(
                request.BroadcasterId,
                rawUserId,
                cancellationToken
            );
            if (result.IsFailure)
            {
                if (attemptId is not null)
                {
                    ChannelEvent? attempt = await _db.ChannelEvents.FindAsync(
                        [attemptId],
                        cancellationToken
                    );
                    if (attempt is not null)
                    {
                        _db.ChannelEvents.Remove(attempt);
                        await _db.SaveChangesAsync(cancellationToken);
                    }
                }
                return NativeFailureOutcome(result, rawUserId);
            }

            ChannelContext? channelCtx = _registry.Get(request.BroadcasterId);
            if (channelCtx is not null)
            {
                DateTimeOffset now = _timeProvider.GetUtcNow();
                channelCtx.LastGlobalShoutout = now;
                channelCtx.LastShoutoutPerUser[rawUserId] = now;
            }
        }

        // The native Helix shoutout carries no visible text. Post the channel's custom announcement only
        // after the native call succeeds, so a Twitch rate limit never announces an unsent shoutout.
        Channel? channel = await _db
            .Channels.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.BroadcasterId, cancellationToken);

        Result announceResult = string.IsNullOrEmpty(request.Announcement)
            ? Result.Success()
            : await _chat.SendAnnouncementAsync(
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

        if (announceResult.IsSuccess)
            await RecordAsync(
                request.BroadcasterId,
                rawUserId,
                replayUncertain ? "replay-uncertain"
                    : request.SkipNative ? "announce-only"
                    : "pipeline",
                cancellationToken
            );

        // TTS is opt-in per invocation (old-bot parity: manual !so speaks it, an automated
        // presence-detection shoutout stays silent by simply never passing tts:true) and best-effort — a
        // synthesis/dispatch failure never fails the shoutout itself. The announcement is the bot's own text, so
        // it speaks in the channel's one bot voice (the default TtsSpeaker.Bot); the target is only the
        // attribution on the request, never the voice. A raid speaks by default (old-bot parity); an explicit tts:false still wins.
        if (announceResult.IsSuccess && request.Speak && channel is not null)
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
                    StreamId: null,
                    Speaker: TtsSpeaker.Bot,
                    SpokenNames: [target.DisplayName, target.Login]
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

        if (announceResult.IsFailure)
        {
            string failure =
                $"the shoutout announcement for {rawUserId} was not posted: {announceResult.ErrorMessage}";
            return IsTransient(announceResult.ErrorCode)
                ? ActionResult.RetryableFailure(failure)
                : ActionResult.Failure(failure);
        }
        return ActionResult.Success(
            replayUncertain ? "announced (native outcome unconfirmed)"
            : request.SkipNative ? "announced (per-user cooldown)"
            : $"shoutout sent to {rawUserId}"
        );
    }

    private async Task RecordAsync(
        Guid broadcasterId,
        string targetUserId,
        string source,
        CancellationToken cancellationToken
    )
    {
        _db.ChannelEvents.Add(
            new ChannelEvent
            {
                Id = Guid.CreateVersion7().ToString(),
                ChannelId = broadcasterId,
                Type = "bot.shoutout.sent",
                Data = JsonSerializer.Serialize(new { toUserId = targetUserId, source }),
                CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
            }
        );
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static ActionResult NativeFailureOutcome(Result native, string rawUserId)
    {
        string failure = $"the native shoutout for {rawUserId} was not sent: {native.ErrorMessage}";
        return IsTransient(native.ErrorCode)
            ? ActionResult.RetryableFailure(failure)
            : ActionResult.Failure(failure);
    }

    internal static string DeliveryKey(
        Guid broadcasterId,
        string targetId,
        DateTimeOffset queuedAt
    ) => $"{broadcasterId:N}:{targetId}:{queuedAt.UtcTicks}";

    internal static string AttemptId(string deliveryKey)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(deliveryKey));
        return "so-try-" + Convert.ToHexString(hash.AsSpan(0, 16));
    }

    // A rate limit or a dropped connection passes by itself; a missing scope or token never does.
    private static bool IsTransient(string? errorCode) =>
        errorCode is TwitchErrorCodes.RateLimited or TwitchErrorCodes.Transport;

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
