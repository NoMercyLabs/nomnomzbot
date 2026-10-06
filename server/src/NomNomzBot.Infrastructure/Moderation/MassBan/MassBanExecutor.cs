// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation.MassBan;

/// <summary>
/// Carries out mass-ban batches, a bounded number of bans at a time. A batch that holds while live is skipped for as
/// long as its channel is live and nobody approved it; it runs once the stream ends. Each ban rides the requesting
/// moderator's own token. Every target is stamped as it is processed, so a restart resumes where it stopped.
/// </summary>
public sealed class MassBanExecutor
{
    private const int MaxErrorLength = 500;

    // Unfinished batches looked at per run; plenty for one sweep across every channel a moderator can hold.
    private const int MaxCandidates = 200;

    private readonly IApplicationDbContext _db;
    private readonly ITwitchModerationApi _moderation;
    private readonly MassBanLiveChannels _liveChannels;
    private readonly IBuiltinResponseComposer _composer;
    private readonly IChatProvider _chat;
    private readonly IOutboundSanctionAccessor _sanctions;
    private readonly TimeProvider _clock;
    private readonly ILogger<MassBanExecutor> _logger;

    public MassBanExecutor(
        IApplicationDbContext db,
        ITwitchModerationApi moderation,
        MassBanLiveChannels liveChannels,
        IBuiltinResponseComposer composer,
        IChatProvider chat,
        IOutboundSanctionAccessor sanctions,
        TimeProvider clock,
        ILogger<MassBanExecutor> logger
    )
    {
        _db = db;
        _moderation = moderation;
        _liveChannels = liveChannels;
        _composer = composer;
        _chat = chat;
        _sanctions = sanctions;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Issues at most <paramref name="maxBans"/> bans from the oldest batch allowed to run; returns how many.</summary>
    public async Task<int> RunAsync(int maxBans, CancellationToken ct = default)
    {
        Guid? batchId = await NextRunnableBatchAsync(ct);
        if (batchId is null)
            return 0;

        MassBanBatch batch = await _db
            .MassBanBatches.Include(b => b.Targets)
            .FirstAsync(b => b.Id == batchId, ct);

        List<MassBanBatchTarget> slice =
        [
            .. batch.Targets.Where(t => t.ProcessedAt == null).Take(maxBans),
        ];
        // The worker has no HTTP request to inherit a sanction from; the moderator who asked for the batch
        // through moderation:ban is the basis, so every ban carries their id to the outbound guard.
        using (
            _sanctions.Begin(OutboundSanction.UserAction("moderation:ban", batch.OperatorUserId))
        )
        {
            foreach (MassBanBatchTarget target in slice)
            {
                await BanAsync(batch, target, ct);
                await _db.SaveChangesAsync(ct);
            }
        }

        if (batch.Targets.All(t => t.ProcessedAt != null))
            await CompleteAsync(batch, ct);

        return slice.Count;
    }

    // The oldest unfinished, undeclined batch that may run now: approved, never held, or its channel is offline.
    // When Twitch cannot say who is live, every held batch keeps waiting — holding is the safe side.
    private async Task<Guid?> NextRunnableBatchAsync(CancellationToken ct)
    {
        var candidates = await _db
            .MassBanBatches.AsNoTracking()
            .Where(b => b.CompletedAt == null && b.DeclinedAt == null)
            .OrderBy(b => b.RequestedAt)
            .Take(MaxCandidates)
            .Select(b => new
            {
                b.Id,
                b.ChannelTwitchId,
                Held = b.HoldWhileLive && b.ApprovedAt == null,
            })
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return null;

        List<string> heldChannels =
        [
            .. candidates.Where(c => c.Held).Select(c => c.ChannelTwitchId),
        ];
        HashSet<string>? live = null;
        if (heldChannels.Count > 0)
        {
            Result<HashSet<string>> lookup = await _liveChannels.FindLiveAsync(heldChannels, ct);
            if (lookup.IsSuccess)
                live = lookup.Value;
        }

        return candidates
            .FirstOrDefault(c => !c.Held || (live is not null && !live.Contains(c.ChannelTwitchId)))
            ?.Id;
    }

    private async Task BanAsync(MassBanBatch batch, MassBanBatchTarget target, CancellationToken ct)
    {
        target.ProcessedAt = _clock.GetUtcNow().UtcDateTime;
        Result<TwitchBanResult> ban = await _moderation.BanAsOperatorAsync(
            batch.OperatorUserId,
            batch.ChannelTwitchId,
            target.TwitchUserId,
            target.Reason,
            ct
        );
        if (ban.IsSuccess || IsAlreadyBanned(ban.ErrorMessage) || IsAlreadyBanned(ban.ErrorDetail))
        {
            target.Banned = true;
            return;
        }

        // The transport's message is only the status; Twitch's own sentence is the detail (2026-10-06).
        string error = string.IsNullOrEmpty(ban.ErrorDetail)
            ? ban.ErrorMessage ?? "Twitch rejected the ban."
            : $"{ban.ErrorMessage} {ban.ErrorDetail}";
        target.Error = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        _logger.LogWarning(
            "Mass ban in {Channel}: {Target} was not banned: {Error}",
            batch.ChannelLogin,
            target.TwitchUserId,
            target.Error
        );
    }

    // Twitch refuses a second ban of the same account; the account is banned all the same.
    private static bool IsAlreadyBanned(string? error) =>
        error is not null && error.Contains("already banned", StringComparison.OrdinalIgnoreCase);

    // Only a chat that was asked hears how it ended; a channel that never heard of the batch gets no message.
    private async Task CompleteAsync(MassBanBatch batch, CancellationToken ct)
    {
        batch.CompletedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
        if (batch.NoticeSentAt is null || batch.ChannelId is not { } channelId)
            return;

        string personality =
            await _db
                .Channels.AsNoTracking()
                .Where(c => c.Id == channelId)
                .Select(c => c.Personality)
                .FirstOrDefaultAsync(ct)
            ?? PersonalityTone.Informative;
        int banned = batch.Targets.Count(t => t.Banned);
        string message = await _composer.ComposeAsync(
            new BuiltinResponseRequest
            {
                BroadcasterId = channelId,
                Personality = personality,
                BuiltinKey = BuiltinResponseSlots.MassBan.Key,
                Slot = BuiltinResponseSlots.MassBan.Completed,
                NeutralFallback =
                    ToneTemplateCatalog.ShippedTemplate(
                        BuiltinResponseSlots.MassBan.Key,
                        BuiltinResponseSlots.MassBan.Completed
                    ) ?? string.Empty,
                Variables = new Dictionary<string, string>
                {
                    ["massban.banned"] = banned.ToString(CultureInfo.InvariantCulture),
                    ["massban.count"] = batch.Targets.Count.ToString(CultureInfo.InvariantCulture),
                },
            },
            ct
        );
        if (message.Length == 0 || !await _chat.SendMessageAsync(channelId, message, ct))
            _logger.LogWarning(
                "Mass ban in {Channel} finished ({Banned} banned) but the closing line was not sent",
                batch.ChannelLogin,
                banned
            );
    }
}
