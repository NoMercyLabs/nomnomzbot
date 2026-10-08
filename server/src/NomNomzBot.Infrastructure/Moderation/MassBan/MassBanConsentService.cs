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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Moderation.MassBan;

/// <inheritdoc />
public sealed class MassBanConsentService : IMassBanConsentService
{
    private const int MaxTargets = 1000;
    private const int MaxReasonLength = 500;
    private const int MaxNoteLength = 500;

    private readonly MassBanChannelPlanner _planner;
    private readonly IOperatorModeratedChannelResolver _channels;
    private readonly IApplicationDbContext _db;
    private readonly IBuiltinResponseComposer _composer;
    private readonly IChatProvider _chat;
    private readonly TimeProvider _clock;
    private readonly ILogger<MassBanConsentService> _logger;

    public MassBanConsentService(
        MassBanChannelPlanner planner,
        IOperatorModeratedChannelResolver channels,
        IApplicationDbContext db,
        IBuiltinResponseComposer composer,
        IChatProvider chat,
        TimeProvider clock,
        ILogger<MassBanConsentService> logger
    )
    {
        _planner = planner;
        _channels = channels;
        _db = db;
        _composer = composer;
        _chat = chat;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<MassBanChannelPreview>>> PreviewAsync(
        Guid operatorUserId,
        MassBanScope scope,
        CancellationToken ct = default
    )
    {
        Result<IReadOnlyList<MassBanChannelPlan>> plan = await _planner.PlanAsync(
            operatorUserId,
            scope,
            ct
        );
        if (plan.IsFailure)
            return plan.WithValue<IReadOnlyList<MassBanChannelPreview>>(default!);

        return Result.Success<IReadOnlyList<MassBanChannelPreview>>([
            .. plan.Value.Select(p => new MassBanChannelPreview(
                p.Channel.BroadcasterId,
                p.Channel.BroadcasterLogin,
                p.Status,
                p.OptIn,
                p.IsOwnChannel,
                p.IsAttacked,
                p.IsLive,
                p.ServedChannel is not null,
                p.RunsAsBroadcaster
            )),
        ]);
    }

    public async Task<Result<MassBanSweepResult>> RequestAsync(
        Guid operatorUserId,
        string operatorDisplayName,
        IReadOnlyList<MassBanTarget> targets,
        MassBanScope scope,
        CancellationToken ct = default
    )
    {
        Result<List<MassBanTarget>> cleaned = Clean(targets);
        if (cleaned.IsFailure)
            return cleaned.WithValue<MassBanSweepResult>(default!);

        Result<IReadOnlyList<MassBanChannelPlan>> plan = await _planner.PlanAsync(
            operatorUserId,
            scope,
            ct
        );
        if (plan.IsFailure)
            return plan.WithValue<MassBanSweepResult>(default!);

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        List<(MassBanChannelPlan Plan, MassBanBatch? Batch)> rows = [];
        foreach (MassBanChannelPlan channel in plan.Value)
        {
            MassBanBatch? batch = channel.IsIncluded
                ? NewBatch(operatorUserId, operatorDisplayName, channel, cleaned.Value, now)
                : null;
            if (batch is not null)
                _db.MassBanBatches.Add(batch);
            rows.Add((channel, batch));
        }
        await _db.SaveChangesAsync(ct);

        List<MassBanChannelOutcome> outcomes = new(rows.Count);
        foreach ((MassBanChannelPlan channel, MassBanBatch? batch) in rows)
        {
            if (batch is null)
            {
                outcomes.Add(new(channel.Channel.BroadcasterLogin, channel.Status, 0));
                continue;
            }
            string status = await AskIfAwaitingAsync(channel, batch, cleaned.Value.Count, ct);
            outcomes.Add(new(channel.Channel.BroadcasterLogin, status, cleaned.Value.Count));
        }

        return Result.Success(new MassBanSweepResult(outcomes));
    }

    public Task<MassBanDecision> ApproveAsync(
        Guid channelId,
        string decidedByDisplayName,
        CancellationToken ct = default
    ) => DecideAsync(channelId, decidedByDisplayName, MassBanDecisionStatus.Approved, ct);

    public Task<MassBanDecision> DeclineAsync(
        Guid channelId,
        string decidedByDisplayName,
        CancellationToken ct = default
    ) => DecideAsync(channelId, decidedByDisplayName, MassBanDecisionStatus.Declined, ct);

    public async Task<Result<ModeratorMassBanOptInRecord>> OptInChannelAsync(
        Guid operatorUserId,
        string broadcasterLogin,
        string note,
        CancellationToken ct = default
    )
    {
        string trimmed = note.Trim();
        if (trimmed.Length == 0)
            return Result.Failure<ModeratorMassBanOptInRecord>(
                "Say where the streamer's permission came from.",
                "VALIDATION_FAILED"
            );
        if (trimmed.Length > MaxNoteLength)
            trimmed = trimmed[..MaxNoteLength];

        // Only a channel Twitch lists this moderator on can be opted in: the ban rides their moderator token.
        Result<IReadOnlyList<TwitchModeratedChannel>> moderated = await _channels.ResolveAsync(
            operatorUserId,
            ct
        );
        if (moderated.IsFailure)
            return moderated.WithValue<ModeratorMassBanOptInRecord>(default!);
        TwitchModeratedChannel? channel = moderated.Value.FirstOrDefault(c =>
            string.Equals(c.BroadcasterLogin, broadcasterLogin, StringComparison.OrdinalIgnoreCase)
        );
        if (channel is null)
            return Result.Failure<ModeratorMassBanOptInRecord>(
                "You are not a moderator of that channel on Twitch.",
                "NOT_FOUND"
            );

        ModeratorMassBanOptIn? existing = await _db.ModeratorMassBanOptIns.FirstOrDefaultAsync(
            o =>
                o.OperatorUserId == operatorUserId
                && o.BroadcasterTwitchId == channel.BroadcasterId,
            ct
        );
        ModeratorMassBanOptIn optIn =
            existing
            ?? new ModeratorMassBanOptIn
            {
                OperatorUserId = operatorUserId,
                BroadcasterTwitchId = channel.BroadcasterId,
            };
        optIn.BroadcasterLogin = channel.BroadcasterLogin;
        optIn.Note = trimmed;
        optIn.RecordedAt = _clock.GetUtcNow().UtcDateTime;
        if (existing is null)
            _db.ModeratorMassBanOptIns.Add(optIn);
        await _db.SaveChangesAsync(ct);
        return Result.Success(ToRecord(optIn));
    }

    public async Task<bool> RemoveOptInAsync(
        Guid operatorUserId,
        string broadcasterLogin,
        CancellationToken ct = default
    )
    {
        List<ModeratorMassBanOptIn> rows = await _db
            .ModeratorMassBanOptIns.Where(o =>
                o.OperatorUserId == operatorUserId
                && o.BroadcasterLogin.ToLower() == broadcasterLogin.ToLower()
            )
            .ToListAsync(ct);
        if (rows.Count == 0)
            return false;
        _db.ModeratorMassBanOptIns.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<MassBanBatchReport>> ListBatchesAsync(
        Guid operatorUserId,
        CancellationToken ct = default
    )
    {
        List<MassBanBatch> batches = await _db
            .MassBanBatches.AsNoTracking()
            .Include(b => b.Targets)
            .Where(b => b.OperatorUserId == operatorUserId)
            .OrderByDescending(b => b.RequestedAt)
            .Take(50)
            .ToListAsync(ct);

        return
        [
            .. batches.Select(b => new MassBanBatchReport(
                b.Id,
                b.ChannelLogin,
                b.RequestedAt,
                b.CompletedAt,
                b.Targets.Count,
                b.Targets.Count(t => t.Banned),
                b.Targets.Count(t => !t.Banned && t.ProcessedAt != null),
                b.Targets.Count(t => t.ProcessedAt == null && t.Attempts > 0),
                b.Targets.Count(t => t.ProcessedAt == null && t.Attempts == 0),
                [
                    .. b
                        .Targets.Where(t => !t.Banned)
                        .Select(t => new MassBanTargetReport(
                            t.TwitchUserId,
                            TargetStatus(t),
                            t.Attempts,
                            t.Error,
                            t.NextAttemptAt
                        )),
                ]
            )),
        ];
    }

    private static string TargetStatus(MassBanBatchTarget target) =>
        target.ProcessedAt != null ? MassBanTargetStatus.Failed
        : target.Attempts > 0 ? MassBanTargetStatus.Retrying
        : MassBanTargetStatus.Waiting;

    public async Task<IReadOnlyList<ModeratorMassBanOptInRecord>> ListOptInsAsync(
        Guid operatorUserId,
        CancellationToken ct = default
    )
    {
        List<ModeratorMassBanOptIn> rows = await _db
            .ModeratorMassBanOptIns.AsNoTracking()
            .Where(o => o.OperatorUserId == operatorUserId)
            .OrderBy(o => o.BroadcasterLogin)
            .ToListAsync(ct);
        return [.. rows.Select(ToRecord)];
    }

    private static ModeratorMassBanOptInRecord ToRecord(ModeratorMassBanOptIn o) =>
        new(o.BroadcasterTwitchId, o.BroadcasterLogin, o.Note, o.RecordedAt);

    // One row per account, a numeric Twitch id each, and a reason Twitch accepts.
    private static Result<List<MassBanTarget>> Clean(IReadOnlyList<MassBanTarget> targets)
    {
        List<MassBanTarget> distinct =
        [
            .. targets
                .DistinctBy(t => t.TwitchUserId)
                .Select(t =>
                    t with
                    {
                        Reason =
                            t.Reason.Length > MaxReasonLength
                                ? t.Reason[..MaxReasonLength]
                                : t.Reason,
                    }
                ),
        ];
        if (distinct.Count is 0 or > MaxTargets)
            return Result.Failure<List<MassBanTarget>>(
                $"A mass ban takes 1 to {MaxTargets} accounts.",
                "VALIDATION_FAILED"
            );
        if (distinct.Any(t => t.TwitchUserId.Length == 0 || !t.TwitchUserId.All(char.IsAsciiDigit)))
            return Result.Failure<List<MassBanTarget>>(
                "Every account must be a numeric Twitch user id.",
                "VALIDATION_FAILED"
            );
        return Result.Success(distinct);
    }

    private static MassBanBatch NewBatch(
        Guid operatorUserId,
        string operatorDisplayName,
        MassBanChannelPlan channel,
        IReadOnlyList<MassBanTarget> targets,
        DateTime now
    ) =>
        new()
        {
            OperatorUserId = operatorUserId,
            OperatorDisplayName = operatorDisplayName,
            ChannelTwitchId = channel.Channel.BroadcasterId,
            ChannelLogin = channel.Channel.BroadcasterLogin,
            ChannelId = channel.ServedChannel?.Id,
            HoldWhileLive = channel.HoldsWhileLive,
            RunsAsBroadcaster = channel.RunsAsBroadcaster,
            RequestedAt = now,
            Targets =
            [
                .. targets.Select(t => new MassBanBatchTarget
                {
                    TwitchUserId = t.TwitchUserId,
                    Reason = t.Reason,
                }),
            ],
        };

    // A live channel the bot serves is told about its held batch; when the notice does not go out, the batch
    // simply waits for the stream to end.
    private async Task<string> AskIfAwaitingAsync(
        MassBanChannelPlan channel,
        MassBanBatch batch,
        int count,
        CancellationToken ct
    )
    {
        if (channel.Status != MassBanChannelStatus.AwaitingApproval)
            return channel.Status;

        if (!await NotifyAsync(channel.ServedChannel!, batch, count, ct))
            return MassBanChannelStatus.HeldUntilOffline;

        batch.NoticeSentAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
        return MassBanChannelStatus.AwaitingApproval;
    }

    private async Task<bool> NotifyAsync(
        Channel channel,
        MassBanBatch batch,
        int count,
        CancellationToken ct
    )
    {
        string message = await _composer.ComposeAsync(
            new BuiltinResponseRequest
            {
                BroadcasterId = channel.Id,
                Personality = channel.Personality,
                BuiltinKey = BuiltinResponseSlots.MassBan.Key,
                Slot = BuiltinResponseSlots.MassBan.Request,
                NeutralFallback =
                    ToneTemplateCatalog.ShippedTemplate(
                        BuiltinResponseSlots.MassBan.Key,
                        BuiltinResponseSlots.MassBan.Request
                    ) ?? string.Empty,
                Variables = new Dictionary<string, string>
                {
                    ["massban.channel"] = channel.Name,
                    ["massban.moderator"] = batch.OperatorDisplayName,
                    ["massban.count"] = count.ToString(CultureInfo.InvariantCulture),
                    ["prefix"] = channel.CommandPrefix,
                },
            },
            ct
        );

        bool sent = message.Length > 0 && await _chat.SendMessageAsync(channel.Id, message, ct);
        if (!sent)
            _logger.LogWarning(
                "Mass-ban notice was not accepted in channel {Channel}; its bans wait for the stream to end",
                channel.Name
            );
        return sent;
    }

    private async Task<MassBanDecision> DecideAsync(
        Guid channelId,
        string decidedByDisplayName,
        MassBanDecisionStatus decision,
        CancellationToken ct
    )
    {
        List<MassBanBatch> open = await _db
            .MassBanBatches.Include(b => b.Targets)
            .Where(b =>
                b.ChannelId == channelId
                && b.HoldWhileLive
                && b.CompletedAt == null
                && b.ApprovedAt == null
                && b.DeclinedAt == null
            )
            .ToListAsync(ct);
        if (open.Count == 0)
            return new(MassBanDecisionStatus.NothingPending, 0);

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        foreach (MassBanBatch batch in open)
        {
            batch.DecidedByDisplayName = decidedByDisplayName;
            if (decision == MassBanDecisionStatus.Approved)
                batch.ApprovedAt = now;
            else
                batch.DeclinedAt = now;
        }
        await _db.SaveChangesAsync(ct);

        int accounts = open.SelectMany(b => b.Targets)
            .Where(t => t.ProcessedAt == null)
            .Select(t => t.TwitchUserId)
            .Distinct()
            .Count();
        return new(decision, accounts);
    }
}
