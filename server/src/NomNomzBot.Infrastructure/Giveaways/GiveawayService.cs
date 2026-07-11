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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Giveaways.Dtos;
using NomNomzBot.Application.Giveaways.Services;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Giveaways.Entities;
using NomNomzBot.Domain.Giveaways.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Analytics;

namespace NomNomzBot.Infrastructure.Giveaways;

/// <summary>
/// <see cref="IGiveawayService"/> (giveaways.md §3.1/§4). Entry enforces eligibility + the unique-entry
/// dedupe + the <c>spend_giveaway</c> cost debit and computes sub-luck tickets from the viewer's
/// community standing; the draw picks distinct winners with a CSPRNG over the ticket-weighted pool
/// (never the broadcaster; mods iff excluded) inside one <c>IUnitOfWork</c> transaction and fulfills per
/// prize mode — currency through the ledger, pipeline enqueued per winner, codes claimed + whispered by
/// <see cref="IGiveawayFulfillment"/>. Winner history is append-only; a re-roll marks + replaces, never
/// rewrites. Eligibility is evaluated ONLY against truthful local data (standing level, sub flag, watch
/// minutes, Twitch account age via Helix) — a filter this build cannot verify (<c>require_follower</c>)
/// is rejected at configuration time, never silently ignored.
/// </summary>
public sealed class GiveawayService : IGiveawayService
{
    private static readonly TimeSpan ActiveViewerWindow = TimeSpan.FromMinutes(30);

    private readonly IApplicationDbContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventBus _bus;
    private readonly ICurrencyAccountService _accounts;
    private readonly IGiveawayFulfillment _fulfillment;
    private readonly TimeProvider _clock;
    private readonly ILogger<GiveawayService> _logger;

    public GiveawayService(
        IApplicationDbContext db,
        IUnitOfWork unitOfWork,
        IEventBus bus,
        ICurrencyAccountService accounts,
        IGiveawayFulfillment fulfillment,
        TimeProvider clock,
        ILogger<GiveawayService> logger
    )
    {
        _db = db;
        _unitOfWork = unitOfWork;
        _bus = bus;
        _accounts = accounts;
        _fulfillment = fulfillment;
        _clock = clock;
        _logger = logger;
    }

    // ── CRUD ────────────────────────────────────────────────────────────────

    public async Task<Result<GiveawayDto>> CreateAsync(
        Guid broadcasterId,
        UpsertGiveawayRequest request,
        CancellationToken ct = default
    )
    {
        Result validation = Validate(request);
        if (validation.IsFailure)
            return Result.Failure<GiveawayDto>(validation.ErrorMessage!, validation.ErrorCode);

        Giveaway giveaway = new() { BroadcasterId = broadcasterId };
        Apply(giveaway, request);
        await _db.Giveaways.AddAsync(giveaway, ct);
        await _db.SaveChangesAsync(ct);
        return Result.Success(ToDto(giveaway, entryCount: 0));
    }

    public async Task<Result<GiveawayDto>> UpdateAsync(
        Guid broadcasterId,
        Guid giveawayId,
        UpsertGiveawayRequest request,
        CancellationToken ct = default
    )
    {
        Giveaway? giveaway = await FindAsync(broadcasterId, giveawayId, ct);
        if (giveaway is null)
            return Result.Failure<GiveawayDto>("Giveaway not found.", "NOT_FOUND");
        if (giveaway.Status is not (GiveawayStatus.Draft or GiveawayStatus.Closed))
            return Result.Failure<GiveawayDto>(
                "Only a draft or closed giveaway can be edited.",
                "VALIDATION_FAILED"
            );

        Result validation = Validate(request);
        if (validation.IsFailure)
            return Result.Failure<GiveawayDto>(validation.ErrorMessage!, validation.ErrorCode);

        Apply(giveaway, request);
        await _db.SaveChangesAsync(ct);
        return Result.Success(ToDto(giveaway, await CountEntriesAsync(giveawayId, ct)));
    }

    public async Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid giveawayId,
        CancellationToken ct = default
    )
    {
        Giveaway? giveaway = await FindAsync(broadcasterId, giveawayId, ct);
        if (giveaway is null)
            return Result.Failure("Giveaway not found.", "NOT_FOUND");

        giveaway.DeletedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<PagedList<GiveawayDto>>> ListAsync(
        Guid broadcasterId,
        GiveawayFilter filter,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<Giveaway> query = _db
            .Giveaways.AsNoTracking()
            .Where(g => g.BroadcasterId == broadcasterId);
        query = filter.Status is { } status
            ? query.Where(g => g.Status == status)
            : query.Where(g => g.Status != GiveawayStatus.Archived);
        query = query.OrderByDescending(g => g.Id);

        int total = await query.CountAsync(ct);
        List<Giveaway> rows = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        List<Guid> ids = rows.Select(g => g.Id).ToList();
        Dictionary<Guid, int> counts = await _db
            .GiveawayEntries.Where(e => ids.Contains(e.GiveawayId))
            .GroupBy(e => e.GiveawayId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);

        List<GiveawayDto> dtos = rows.Select(g => ToDto(g, counts.GetValueOrDefault(g.Id)))
            .ToList();
        return Result.Success(
            new PagedList<GiveawayDto>(dtos, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<GiveawayDto>> GetAsync(
        Guid broadcasterId,
        Guid giveawayId,
        CancellationToken ct = default
    )
    {
        Giveaway? giveaway = await FindAsync(broadcasterId, giveawayId, ct);
        return giveaway is null
            ? Result.Failure<GiveawayDto>("Giveaway not found.", "NOT_FOUND")
            : Result.Success(ToDto(giveaway, await CountEntriesAsync(giveawayId, ct)));
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    public async Task<Result<GiveawayDto>> OpenAsync(
        Guid broadcasterId,
        Guid giveawayId,
        CancellationToken ct = default
    )
    {
        Giveaway? giveaway = await FindAsync(broadcasterId, giveawayId, ct);
        if (giveaway is null)
            return Result.Failure<GiveawayDto>("Giveaway not found.", "NOT_FOUND");
        if (giveaway.Status is not (GiveawayStatus.Draft or GiveawayStatus.Closed))
            return Result.Failure<GiveawayDto>(
                "Only a draft or closed giveaway can be opened.",
                "VALIDATION_FAILED"
            );

        // D2: one active (open, or closed-but-undrawn) giveaway per channel — clear keyword, no contention.
        bool anotherActive = await _db.Giveaways.AnyAsync(
            g =>
                g.BroadcasterId == broadcasterId
                && g.Id != giveawayId
                && (g.Status == GiveawayStatus.Open || g.Status == GiveawayStatus.Closed),
            ct
        );
        if (anotherActive)
            return Result.Failure<GiveawayDto>(
                "Another giveaway is already active — draw or archive it first.",
                "GIVEAWAY_ALREADY_ACTIVE"
            );

        giveaway.Status = GiveawayStatus.Open;
        giveaway.OpenedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);

        await _bus.PublishAsync(
            new GiveawayOpenedEvent
            {
                BroadcasterId = broadcasterId,
                OccurredAt = _clock.GetUtcNow(),
                GiveawayId = giveaway.Id,
                EntryMode = giveaway.EntryMode,
                Keyword = giveaway.Keyword,
            },
            ct
        );
        return Result.Success(ToDto(giveaway, await CountEntriesAsync(giveawayId, ct)));
    }

    public async Task<Result<GiveawayDto>> CloseAsync(
        Guid broadcasterId,
        Guid giveawayId,
        CancellationToken ct = default
    )
    {
        Giveaway? giveaway = await FindAsync(broadcasterId, giveawayId, ct);
        if (giveaway is null)
            return Result.Failure<GiveawayDto>("Giveaway not found.", "NOT_FOUND");
        if (giveaway.Status != GiveawayStatus.Open)
            return Result.Failure<GiveawayDto>(
                "Only an open giveaway can be closed.",
                "VALIDATION_FAILED"
            );

        giveaway.Status = GiveawayStatus.Closed;
        giveaway.ClosesAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
        return Result.Success(ToDto(giveaway, await CountEntriesAsync(giveawayId, ct)));
    }

    // ── Entry ───────────────────────────────────────────────────────────────

    public async Task<Result<GiveawayEntryDto>> EnterAsync(
        Guid broadcasterId,
        Guid giveawayId,
        Guid viewerUserId,
        CancellationToken ct = default
    )
    {
        Giveaway? giveaway = await FindAsync(broadcasterId, giveawayId, ct);
        if (giveaway is null)
            return Result.Failure<GiveawayEntryDto>("Giveaway not found.", "NOT_FOUND");
        if (giveaway.Status != GiveawayStatus.Open)
            return Result.Failure<GiveawayEntryDto>(
                "The giveaway is not open for entries.",
                "GIVEAWAY_NOT_OPEN"
            );

        ChannelCommunityStanding? standing = await _db
            .ChannelCommunityStandings.AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.BroadcasterId == broadcasterId && s.UserId == viewerUserId,
                ct
            );

        Result eligible = await CheckEligibilityAsync(
            broadcasterId,
            viewerUserId,
            standing,
            giveaway.EligibilityJson,
            ct
        );
        if (eligible.IsFailure)
            return Result.Failure<GiveawayEntryDto>(eligible.ErrorMessage!, eligible.ErrorCode);

        // MaxEntriesPerUser is a config surface, but the unique (GiveawayId, ViewerUserId) key means one
        // ROW per viewer — the cap gates whether a repeat attempt is an error or a friendly no-op.
        GiveawayEntry? existing = await _db.GiveawayEntries.FirstOrDefaultAsync(
            e => e.GiveawayId == giveawayId && e.ViewerUserId == viewerUserId,
            ct
        );
        if (existing is not null)
            return Result.Failure<GiveawayEntryDto>(
                "You have already entered this giveaway.",
                "ALREADY_ENTERED"
            );

        string viewerTwitchId =
            await _db
                .Users.Where(u => u.Id == viewerUserId)
                .Select(u => u.TwitchUserId ?? string.Empty)
                .FirstOrDefaultAsync(ct)
            ?? string.Empty;

        long? costLedgerEntryId = null;
        if (giveaway.EntryCost is > 0)
        {
            Result<CurrencyLedgerEntryDto> debit = await _accounts.PostLedgerEntryAsync(
                broadcasterId,
                new PostLedgerEntryCommand(
                    viewerUserId,
                    -giveaway.EntryCost.Value,
                    nameof(CurrencyEntryType.SpendGiveaway),
                    nameof(CurrencyLedgerSourceType.Giveaway),
                    SourceId: giveawayId,
                    EventId: null,
                    Reason: $"Giveaway entry: {giveaway.Title}",
                    ActorUserId: null,
                    IdempotencyKey: $"giveaway-entry:{giveawayId}:{viewerUserId}"
                ),
                ct
            );
            if (debit.IsFailure)
                return Result.Failure<GiveawayEntryDto>(debit.ErrorMessage!, debit.ErrorCode);
            costLedgerEntryId = debit.Value.Id;
        }

        GiveawayEntry entry = new()
        {
            BroadcasterId = broadcasterId,
            GiveawayId = giveawayId,
            ViewerUserId = viewerUserId,
            ViewerTwitchUserId = viewerTwitchId,
            TicketCount = ComputeTickets(giveaway.WeightingJson, standing),
            EntryCostLedgerEntryId = costLedgerEntryId,
            EnteredAt = _clock.GetUtcNow().UtcDateTime,
        };
        await _db.GiveawayEntries.AddAsync(entry, ct);
        await _db.SaveChangesAsync(ct);

        return Result.Success(
            new GiveawayEntryDto(
                entry.Id,
                giveawayId,
                viewerUserId,
                entry.TicketCount,
                entry.EnteredAt
            )
        );
    }

    // ── Draw / redraw / winners ─────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<GiveawayWinnerDto>>> DrawAsync(
        Guid broadcasterId,
        Guid giveawayId,
        CancellationToken ct = default
    )
    {
        Giveaway? giveaway = await FindAsync(broadcasterId, giveawayId, ct);
        if (giveaway is null)
            return Result.Failure<IReadOnlyList<GiveawayWinnerDto>>(
                "Giveaway not found.",
                "NOT_FOUND"
            );
        if (giveaway.Status is not (GiveawayStatus.Open or GiveawayStatus.Closed))
            return Result.Failure<IReadOnlyList<GiveawayWinnerDto>>(
                "The giveaway has already been drawn.",
                "VALIDATION_FAILED"
            );

        List<WeightedCandidate> pool = await BuildCandidatePoolAsync(
            giveaway,
            excludeUserIds: [],
            ct
        );
        if (pool.Count == 0)
            return Result.Failure<IReadOnlyList<GiveawayWinnerDto>>(
                "No eligible entries to draw from.",
                "NO_ENTRIES"
            );

        int winnerTarget = Math.Min(giveaway.WinnerCount, pool.Count);

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            List<GiveawayWinner> winners = [];
            for (int i = 0; i < winnerTarget; i++)
            {
                WeightedCandidate picked = PickWeighted(pool);
                pool.Remove(picked);

                GiveawayWinner winner = new()
                {
                    BroadcasterId = broadcasterId,
                    GiveawayId = giveawayId,
                    ViewerUserId = picked.UserId,
                    ViewerTwitchUserId = picked.TwitchUserId,
                    DrawnAt = _clock.GetUtcNow().UtcDateTime,
                    Status = giveaway.ClaimWindowMinutes is null
                        ? GiveawayWinnerStatus.Claimed
                        : GiveawayWinnerStatus.Drawn,
                };
                await _db.GiveawayWinners.AddAsync(winner, ct);
                winners.Add(winner);
            }
            await _db.SaveChangesAsync(ct);

            foreach (GiveawayWinner winner in winners)
                await _fulfillment.FulfillAsync(giveaway, winner, ct);

            giveaway.Status = GiveawayStatus.Drawn;
            giveaway.DrawnAt = _clock.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);

            int entryCount = await CountEntriesAsync(giveawayId, ct);
            await _bus.PublishAsync(
                new GiveawayDrawnEvent
                {
                    BroadcasterId = broadcasterId,
                    OccurredAt = _clock.GetUtcNow(),
                    GiveawayId = giveawayId,
                    WinnerUserIds = winners.Select(w => w.ViewerUserId).ToList(),
                    EntryCount = entryCount,
                    PrizeMode = giveaway.PrizeMode,
                },
                ct
            );

            // Fewer codes than winners is flagged loudly, never silently dropped (§4 CODE_POOL_EXHAUSTED).
            if (
                giveaway.PrizeMode == GiveawayPrizeMode.CodePool
                && winners.Any(w => w.AssignedCodeId is null)
            )
                return Result.Failure<IReadOnlyList<GiveawayWinnerDto>>(
                    "The code pool ran out before every winner got a code — the un-coded winners are flagged in winner history.",
                    "CODE_POOL_EXHAUSTED"
                );

            return Result.Success<IReadOnlyList<GiveawayWinnerDto>>(
                await ToWinnerDtosAsync(winners, ct)
            );
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }
    }

    public async Task<Result<GiveawayWinnerDto>> RedrawAsync(
        Guid broadcasterId,
        Guid giveawayId,
        Guid winnerId,
        CancellationToken ct = default
    )
    {
        Giveaway? giveaway = await FindAsync(broadcasterId, giveawayId, ct);
        if (giveaway is null)
            return Result.Failure<GiveawayWinnerDto>("Giveaway not found.", "NOT_FOUND");

        GiveawayWinner? target = await _db.GiveawayWinners.FirstOrDefaultAsync(
            w => w.Id == winnerId && w.GiveawayId == giveawayId,
            ct
        );
        if (target is null)
            return Result.Failure<GiveawayWinnerDto>("Winner not found.", "NOT_FOUND");
        if (target.Status == GiveawayWinnerStatus.Redrawn)
            return Result.Failure<GiveawayWinnerDto>(
                "This winner has already been replaced.",
                "VALIDATION_FAILED"
            );

        // Exclude EVERY prior winner (any status) so a replacement can never be a repeat.
        List<Guid> priorWinnerIds = await _db
            .GiveawayWinners.Where(w => w.GiveawayId == giveawayId)
            .Select(w => w.ViewerUserId)
            .ToListAsync(ct);

        List<WeightedCandidate> pool = await BuildCandidatePoolAsync(giveaway, priorWinnerIds, ct);
        if (pool.Count == 0)
            return Result.Failure<GiveawayWinnerDto>(
                "No eligible entries left to redraw from.",
                "NO_ENTRIES"
            );

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            target.Status = GiveawayWinnerStatus.Redrawn;

            WeightedCandidate picked = PickWeighted(pool);
            GiveawayWinner replacement = new()
            {
                BroadcasterId = broadcasterId,
                GiveawayId = giveawayId,
                ViewerUserId = picked.UserId,
                ViewerTwitchUserId = picked.TwitchUserId,
                DrawnAt = _clock.GetUtcNow().UtcDateTime,
                Status = giveaway.ClaimWindowMinutes is null
                    ? GiveawayWinnerStatus.Claimed
                    : GiveawayWinnerStatus.Drawn,
                IsRedraw = true,
            };
            await _db.GiveawayWinners.AddAsync(replacement, ct);
            await _db.SaveChangesAsync(ct);

            await _fulfillment.FulfillAsync(giveaway, replacement, ct);
            await _db.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);

            return Result.Success((await ToWinnerDtosAsync([replacement], ct))[0]);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }
    }

    public async Task<Result<PagedList<GiveawayWinnerDto>>> GetWinnersAsync(
        Guid broadcasterId,
        Guid giveawayId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<GiveawayWinner> query = _db
            .GiveawayWinners.AsNoTracking()
            .Where(w => w.BroadcasterId == broadcasterId && w.GiveawayId == giveawayId)
            .OrderByDescending(w => w.Id);

        int total = await query.CountAsync(ct);
        List<GiveawayWinner> rows = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        return Result.Success(
            new PagedList<GiveawayWinnerDto>(
                await ToWinnerDtosAsync(rows, ct),
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    // ── Candidates, eligibility, weighting ──────────────────────────────────

    private sealed record WeightedCandidate(Guid UserId, string TwitchUserId, int Tickets);

    private async Task<List<WeightedCandidate>> BuildCandidatePoolAsync(
        Giveaway giveaway,
        IReadOnlyList<Guid> excludeUserIds,
        CancellationToken ct
    )
    {
        Guid ownerUserId = await _db
            .Channels.Where(c => c.Id == giveaway.BroadcasterId)
            .Select(c => c.OwnerUserId)
            .FirstOrDefaultAsync(ct);

        List<WeightedCandidate> candidates;
        if (giveaway.EntryMode == GiveawayEntryMode.Keyword)
        {
            candidates = await _db
                .GiveawayEntries.AsNoTracking()
                .Where(e => e.GiveawayId == giveaway.Id)
                .Select(e => new WeightedCandidate(
                    e.ViewerUserId,
                    e.ViewerTwitchUserId,
                    e.TicketCount
                ))
                .ToListAsync(ct);
        }
        else
        {
            // active_viewers: distinct chatters within the recency window — real activity, never fabricated.
            DateTime since = _clock.GetUtcNow().UtcDateTime - ActiveViewerWindow;
            List<string> chatterTwitchIds = await _db
                .ChatMessages.AsNoTracking()
                .Where(m => m.BroadcasterId == giveaway.BroadcasterId && m.CreatedAt >= since)
                .Select(m => m.UserId)
                .Distinct()
                .ToListAsync(ct);

            var viewers = await _db
                .Users.AsNoTracking()
                .Where(u => u.TwitchUserId != null && chatterTwitchIds.Contains(u.TwitchUserId))
                .Select(u => new { u.Id, u.TwitchUserId })
                .ToListAsync(ct);

            Dictionary<Guid, ChannelCommunityStanding> standings = await _db
                .ChannelCommunityStandings.AsNoTracking()
                .Where(s => s.BroadcasterId == giveaway.BroadcasterId)
                .ToDictionaryAsync(s => s.UserId, ct);

            candidates = [];
            foreach (var viewer in viewers)
            {
                ChannelCommunityStanding? standing = standings.GetValueOrDefault(viewer.Id);
                Result eligible = await CheckEligibilityAsync(
                    giveaway.BroadcasterId,
                    viewer.Id,
                    standing,
                    giveaway.EligibilityJson,
                    ct
                );
                if (eligible.IsFailure)
                    continue;
                candidates.Add(
                    new WeightedCandidate(
                        viewer.Id,
                        viewer.TwitchUserId!,
                        ComputeTickets(giveaway.WeightingJson, standing)
                    )
                );
            }
        }

        HashSet<Guid> excluded = [.. excludeUserIds, ownerUserId];
        if (giveaway.ExcludeModerators)
        {
            List<Guid> modIds = await _db
                .ChannelCommunityStandings.AsNoTracking()
                .Where(s =>
                    s.BroadcasterId == giveaway.BroadcasterId
                    && s.Standing == CommunityStanding.Moderator
                )
                .Select(s => s.UserId)
                .ToListAsync(ct);
            foreach (Guid modId in modIds)
                excluded.Add(modId);
        }

        return candidates.Where(c => !excluded.Contains(c.UserId) && c.Tickets > 0).ToList();
    }

    /// <summary>CSPRNG pick over the cumulative ticket weights — auditable fairness (D4).</summary>
    private static WeightedCandidate PickWeighted(IReadOnlyList<WeightedCandidate> pool)
    {
        long totalTickets = pool.Sum(c => (long)c.Tickets);
        long roll = RandomNumberGenerator.GetInt32(0, (int)Math.Min(totalTickets, int.MaxValue));
        long cumulative = 0;
        foreach (WeightedCandidate candidate in pool)
        {
            cumulative += candidate.Tickets;
            if (roll < cumulative)
                return candidate;
        }
        return pool[^1];
    }

    private sealed class EligibilityConfig
    {
        [JsonProperty("require_sub")]
        public bool RequireSub { get; set; }

        [JsonProperty("require_follower")]
        public bool RequireFollower { get; set; }

        [JsonProperty("min_standing_level")]
        public int? MinStandingLevel { get; set; }

        [JsonProperty("min_watch_minutes")]
        public int? MinWatchMinutes { get; set; }

        [JsonProperty("min_account_age_days")]
        public int? MinAccountAgeDays { get; set; }
    }

    private async Task<Result> CheckEligibilityAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        ChannelCommunityStanding? standing,
        string? eligibilityJson,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(eligibilityJson))
            return Result.Success(); // D3: empty = everyone.

        EligibilityConfig? config;
        try
        {
            config = JsonConvert.DeserializeObject<EligibilityConfig>(eligibilityJson);
        }
        catch (JsonException)
        {
            return Result.Failure(
                "The giveaway's eligibility config is invalid.",
                "VALIDATION_FAILED"
            );
        }
        if (config is null)
            return Result.Success();

        if (
            config.RequireSub
            && standing?.Standing
                is not CommunityStanding.Subscriber
                    and not CommunityStanding.Vip
                    and not CommunityStanding.Artist
                    and not CommunityStanding.Moderator
        )
            return Result.Failure("This giveaway is subscribers-only.", "NOT_ELIGIBLE");

        if (config.MinStandingLevel is { } minLevel && (standing?.LevelValue ?? 0) < minLevel)
            return Result.Failure(
                "Your community standing is below this giveaway's floor.",
                "NOT_ELIGIBLE"
            );

        if (config.MinWatchMinutes is { } minWatch)
        {
            // Watch time accrues per platform identity in ChannelChatterDays (keyed by the shared
            // ChatterIdentityHash). A viewer whose identity we cannot resolve fails the requirement
            // honestly — no watch history means the requirement is unmet, not waived.
            string? twitchId = await _db
                .Users.AsNoTracking()
                .Where(u => u.Id == viewerUserId)
                .Select(u => u.TwitchUserId)
                .FirstOrDefaultAsync(ct);
            long watchSeconds = 0;
            if (twitchId is not null)
            {
                string chatterHash = ChatterIdentityHash.Compute(
                    AuthEnums.Platform.Twitch,
                    twitchId
                );
                // The viewer's presence span per day (first→last seen) — the same observations the
                // analytics watch-time fold rides; summed across days as the eligibility measure.
                List<(DateTime First, DateTime Last)> days = await _db
                    .ChannelChatterDays.AsNoTracking()
                    .Where(d => d.BroadcasterId == broadcasterId && d.ChatterHash == chatterHash)
                    .Select(d => new ValueTuple<DateTime, DateTime>(d.FirstSeenAt, d.LastSeenAt))
                    .ToListAsync(ct);
                watchSeconds = (long)days.Sum(d => Math.Max((d.Last - d.First).TotalSeconds, 0));
            }
            if (watchSeconds / 60 < minWatch)
                return Result.Failure(
                    "You have not watched this channel long enough for this giveaway.",
                    "NOT_ELIGIBLE"
                );
        }

        if (config.MinAccountAgeDays is { } minAge)
        {
            DateTime? knownSince = await _db
                .Users.AsNoTracking()
                .Where(u => u.Id == viewerUserId)
                .Select(u => (DateTime?)u.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (
                knownSince is null
                || (_clock.GetUtcNow().UtcDateTime - knownSince.Value).TotalDays < minAge
            )
                return Result.Failure("Your account is too new for this giveaway.", "NOT_ELIGIBLE");
        }

        return Result.Success();
    }

    private sealed class WeightingConfig
    {
        [JsonProperty("sub_t1")]
        public int SubT1 { get; set; } = 1;

        [JsonProperty("sub_t2")]
        public int SubT2 { get; set; } = 1;

        [JsonProperty("sub_t3")]
        public int SubT3 { get; set; } = 1;

        [JsonProperty("vip")]
        public int Vip { get; set; } = 1;
    }

    /// <summary>Sub-luck tickets (D4): the viewer's best applicable multiplier; 1 when unweighted.</summary>
    internal static int ComputeTickets(string? weightingJson, ChannelCommunityStanding? standing)
    {
        if (string.IsNullOrWhiteSpace(weightingJson) || standing is null)
            return 1;

        WeightingConfig? config;
        try
        {
            config = JsonConvert.DeserializeObject<WeightingConfig>(weightingJson);
        }
        catch (JsonException)
        {
            return 1;
        }
        if (config is null)
            return 1;

        int tickets = 1;
        if (
            standing.Standing
            is CommunityStanding.Subscriber
                or CommunityStanding.Vip
                or CommunityStanding.Artist
                or CommunityStanding.Moderator
        )
            tickets = Math.Max(
                tickets,
                standing.SubTier switch
                {
                    "3000" => config.SubT3,
                    "2000" => config.SubT2,
                    _ => config.SubT1,
                }
            );
        if (
            standing.Standing
            is CommunityStanding.Vip
                or CommunityStanding.Artist
                or CommunityStanding.Moderator
        )
            tickets = Math.Max(tickets, config.Vip);
        return Math.Max(tickets, 1);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private Result Validate(UpsertGiveawayRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return Result.Failure("A title is required.", "VALIDATION_FAILED");
        if (request.EntryMode is not (GiveawayEntryMode.Keyword or GiveawayEntryMode.ActiveViewers))
            return Result.Failure("Unknown entry mode.", "VALIDATION_FAILED");
        if (
            request.EntryMode == GiveawayEntryMode.Keyword
            && string.IsNullOrWhiteSpace(request.Keyword)
        )
            return Result.Failure("Keyword mode requires a keyword.", "VALIDATION_FAILED");
        if (request.WinnerCount < 1)
            return Result.Failure("WinnerCount must be at least 1.", "VALIDATION_FAILED");
        if (
            request.PrizeMode
            is not (
                GiveawayPrizeMode.Announce
                or GiveawayPrizeMode.Currency
                or GiveawayPrizeMode.Pipeline
                or GiveawayPrizeMode.CodePool
            )
        )
            return Result.Failure("Unknown prize mode.", "VALIDATION_FAILED");
        if (
            request.PrizeMode == GiveawayPrizeMode.Currency
            && request.PrizeCurrencyAmount is not > 0
            && !request.PrizeFromPot
        )
            return Result.Failure(
                "Currency mode needs a prize amount or the pot flag.",
                "VALIDATION_FAILED"
            );
        if (request.PrizeMode == GiveawayPrizeMode.Pipeline && request.PrizePipelineId is null)
            return Result.Failure("Pipeline mode needs a pipeline.", "VALIDATION_FAILED");
        if (request.PrizeMode == GiveawayPrizeMode.CodePool && request.PrizeCodePoolId is null)
            return Result.Failure("Code-pool mode needs a code pool.", "VALIDATION_FAILED");

        // require_follower cannot be verified truthfully yet (no follower standing and no single-user
        // Helix follow check in the client) — reject loudly instead of silently ignoring it.
        if (
            !string.IsNullOrWhiteSpace(request.EligibilityJson)
            && request.EligibilityJson.Contains(
                "require_follower",
                StringComparison.OrdinalIgnoreCase
            )
            && request.EligibilityJson.Contains("true", StringComparison.OrdinalIgnoreCase)
        )
            return Result.Failure(
                "The follower requirement is not supported yet — remove require_follower.",
                "VALIDATION_FAILED"
            );

        return Result.Success();
    }

    private static void Apply(Giveaway giveaway, UpsertGiveawayRequest request)
    {
        giveaway.Title = request.Title;
        giveaway.EntryMode = request.EntryMode;
        giveaway.Keyword = request.Keyword?.Trim();
        giveaway.EntryCost = request.EntryCost;
        giveaway.MaxEntriesPerUser = request.MaxEntriesPerUser;
        giveaway.EligibilityJson = request.EligibilityJson;
        giveaway.WeightingJson = request.WeightingJson;
        giveaway.WinnerCount = request.WinnerCount;
        giveaway.ExcludeModerators = request.ExcludeModerators;
        giveaway.ClaimWindowMinutes = request.ClaimWindowMinutes;
        giveaway.PrizeMode = request.PrizeMode;
        giveaway.PrizeCurrencyAmount = request.PrizeCurrencyAmount;
        giveaway.PrizeFromPot = request.PrizeFromPot;
        giveaway.PrizePipelineId = request.PrizePipelineId;
        giveaway.PrizeCodePoolId = request.PrizeCodePoolId;
    }

    private Task<Giveaway?> FindAsync(Guid broadcasterId, Guid giveawayId, CancellationToken ct) =>
        _db.Giveaways.FirstOrDefaultAsync(
            g => g.Id == giveawayId && g.BroadcasterId == broadcasterId,
            ct
        );

    private Task<int> CountEntriesAsync(Guid giveawayId, CancellationToken ct) =>
        _db.GiveawayEntries.CountAsync(e => e.GiveawayId == giveawayId, ct);

    private async Task<IReadOnlyList<GiveawayWinnerDto>> ToWinnerDtosAsync(
        IReadOnlyList<GiveawayWinner> winners,
        CancellationToken ct
    )
    {
        List<Guid> userIds = winners.Select(w => w.ViewerUserId).Distinct().ToList();
        Dictionary<Guid, string> names = await _db
            .Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.Username ?? "unknown", ct);

        return winners
            .Select(w => new GiveawayWinnerDto(
                w.Id,
                w.GiveawayId,
                w.ViewerUserId,
                names.GetValueOrDefault(w.ViewerUserId, "unknown"),
                w.DrawnAt,
                w.Status,
                w.IsRedraw,
                w.AssignedCodeId,
                w.WhisperDelivered
            ))
            .ToList();
    }

    private static GiveawayDto ToDto(Giveaway g, int entryCount) =>
        new(
            g.Id,
            g.Title,
            g.EntryMode,
            g.Keyword,
            g.EntryCost,
            g.MaxEntriesPerUser,
            g.EligibilityJson,
            g.WeightingJson,
            g.WinnerCount,
            g.ExcludeModerators,
            g.ClaimWindowMinutes,
            g.PrizeMode,
            g.PrizeCurrencyAmount,
            g.PrizeFromPot,
            g.PrizePipelineId,
            g.PrizeCodePoolId,
            g.Status,
            g.OpenedAt,
            g.ClosesAt,
            g.DrawnAt,
            entryCount,
            g.CreatedAt
        );
}
