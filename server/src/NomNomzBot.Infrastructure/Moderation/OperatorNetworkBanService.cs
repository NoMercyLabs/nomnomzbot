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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Services;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// Fans a ban out across every channel Twitch says the operator moderates (chat-client.md §3.5). The channel set
/// comes from Twitch's Get Moderated Channels for the operator — not the local DB — and each ban rides the operator's
/// OWN token via <see cref="ITwitchModerationApi.BanAsOperatorAsync"/>. Best-effort and per-channel: a channel that
/// fails is recorded and the sweep continues, so one rate-limited or no-longer-moderated channel never aborts the rest.
/// </summary>
public sealed class OperatorNetworkBanService : IOperatorNetworkBanService
{
    private readonly IChannelAccessService _channelAccess;
    private readonly ITwitchModeratorsApi _moderators;
    private readonly ITwitchModerationApi _moderation;
    private readonly IApplicationDbContext _db;
    private readonly ILogger<OperatorNetworkBanService> _logger;

    // Twitch's own bounds for a blocked term (Add Blocked Term: "a minimum of 2 characters … a maximum of 500").
    private const int MinTermLength = 2;
    private const int MaxTermLength = 500;

    public OperatorNetworkBanService(
        IChannelAccessService channelAccess,
        ITwitchModeratorsApi moderators,
        ITwitchModerationApi moderation,
        IApplicationDbContext db,
        ILogger<OperatorNetworkBanService> logger
    )
    {
        _channelAccess = channelAccess;
        _moderators = moderators;
        _moderation = moderation;
        _db = db;
        _logger = logger;
    }

    public Task<Result<NetworkBanResult>> BanAcrossModeratedAsync(
        Guid operatorUserId,
        string targetTwitchUserId,
        string? reason,
        CancellationToken ct = default
    ) =>
        FanOutAsync(
            operatorUserId,
            "ban",
            async channel =>
            {
                Result<TwitchBanResult> ban = await _moderation.BanAsOperatorAsync(
                    operatorUserId,
                    channel.BroadcasterId,
                    targetTwitchUserId,
                    reason,
                    ct
                );
                return ban.IsSuccess
                    ? Result.Success()
                    : Result.Failure(
                        ban.ErrorMessage ?? "Twitch rejected the ban.",
                        ban.ErrorCode ?? "TWITCH_ERROR"
                    );
            },
            ct
        );

    public Task<Result<NetworkBanResult>> UnbanAcrossModeratedAsync(
        Guid operatorUserId,
        string targetTwitchUserId,
        CancellationToken ct = default
    ) =>
        FanOutAsync(
            operatorUserId,
            "unban",
            channel =>
                _moderation.UnbanAsOperatorAsync(
                    operatorUserId,
                    channel.BroadcasterId,
                    targetTwitchUserId,
                    ct
                ),
            ct
        );

    public Task<Result<NetworkBanResult>> BlockTermAcrossModeratedAsync(
        Guid operatorUserId,
        string text,
        CancellationToken ct = default
    )
    {
        string term = text.Trim();
        if (term.Length is < MinTermLength or > MaxTermLength)
            return Task.FromResult(InvalidTerm());

        return FanOutAsync(
            operatorUserId,
            "block-term",
            async channel =>
            {
                Result<TwitchBlockedTerm> added = await _moderation.AddBlockedTermAsOperatorAsync(
                    operatorUserId,
                    channel.BroadcasterId,
                    term,
                    ct
                );
                return added.IsSuccess
                    ? Result.Success()
                    : Result.Failure(
                        added.ErrorMessage ?? "Twitch rejected the blocked term.",
                        added.ErrorCode ?? "TWITCH_ERROR"
                    );
            },
            ct
        );
    }

    public Task<Result<NetworkBanResult>> UnblockTermAcrossModeratedAsync(
        Guid operatorUserId,
        string text,
        CancellationToken ct = default
    )
    {
        string term = text.Trim();
        if (term.Length is < MinTermLength or > MaxTermLength)
            return Task.FromResult(InvalidTerm());

        return FanOutAsync(
            operatorUserId,
            "unblock-term",
            async channel =>
            {
                Result<string?> termId = await FindBlockedTermIdAsync(
                    operatorUserId,
                    channel.BroadcasterId,
                    term,
                    ct
                );
                if (termId.IsFailure)
                    return termId;
                return termId.Value is null
                    ? Result.Success()
                    : await _moderation.RemoveBlockedTermAsOperatorAsync(
                        operatorUserId,
                        channel.BroadcasterId,
                        termId.Value,
                        ct
                    );
            },
            ct
        );
    }

    private static Result<NetworkBanResult> InvalidTerm() =>
        Result.Failure<NetworkBanResult>(
            $"A blocked term must be {MinTermLength} to {MaxTermLength} characters.",
            "VALIDATION_FAILED"
        );

    // Remove Blocked Term takes the term's id, not its text, so each channel's list is paged until the text matches.
    private async Task<Result<string?>> FindBlockedTermIdAsync(
        Guid operatorUserId,
        string broadcasterTwitchId,
        string term,
        CancellationToken ct
    )
    {
        string? cursor = null;
        do
        {
            Result<TwitchPage<TwitchBlockedTerm>> page =
                await _moderation.GetBlockedTermsAsOperatorAsync(
                    operatorUserId,
                    broadcasterTwitchId,
                    new(After: cursor),
                    ct
                );
            if (page.IsFailure)
                return page.WithValue<string?>(null);

            TwitchBlockedTerm? match = page.Value.Items.FirstOrDefault(blocked =>
                string.Equals(blocked.Text, term, StringComparison.OrdinalIgnoreCase)
            );
            if (match is not null)
                return Result.Success<string?>(match.Id);

            cursor = page.Value.NextCursor;
        } while (!string.IsNullOrEmpty(cursor));

        return Result.Success<string?>(null);
    }

    // The shared fan-out both directions ride: resolve the operator's Twitch-authoritative moderated-channel set
    // (Get Moderated Channels, not the local DB), apply <paramref name="perChannel"/> to each AS THE OPERATOR, and
    // aggregate — best-effort, so a channel that fails is recorded and the sweep continues. An operator who owns no
    // channel moderates nothing; a failure to even LIST the channels surfaces (never a silent empty success).
    private async Task<Result<NetworkBanResult>> FanOutAsync(
        Guid operatorUserId,
        string action,
        Func<TwitchModeratedChannel, Task<Result>> perChannel,
        CancellationToken ct
    )
    {
        Guid operatorChannelId = await _channelAccess.ResolveOwnChannelAsync(
            operatorUserId.ToString(),
            ct
        );
        if (operatorChannelId == Guid.Empty)
            return Result.Success(new NetworkBanResult(0, 0, []));

        Result<IReadOnlyList<TwitchModeratedChannel>> channels = await ResolveChannelsAsync(
            operatorChannelId,
            ct
        );
        if (channels.IsFailure)
            return channels.WithValue<NetworkBanResult>(default!);

        List<ChannelBanOutcome> outcomes = new(channels.Value.Count);
        foreach (TwitchModeratedChannel channel in channels.Value)
        {
            Result outcome = await perChannel(channel);

            outcomes.Add(
                new(
                    channel.BroadcasterLogin,
                    outcome.IsSuccess,
                    outcome.IsSuccess ? null : outcome.ErrorMessage
                )
            );

            if (outcome.IsFailure)
                _logger.LogWarning(
                    "Network {Action}: operator {Operator} could not act in {Channel}: {Error}",
                    action,
                    operatorUserId,
                    channel.BroadcasterLogin,
                    outcome.ErrorMessage
                );
        }

        int succeeded = outcomes.Count(outcome => outcome.Succeeded);
        return Result.Success(new NetworkBanResult(outcomes.Count, succeeded, outcomes));
    }

    // "Every channel I moderate" includes the one the operator owns: Get Moderated Channels never lists it, so it is
    // added first from the local channel row (skipped while that row has no Twitch id yet).
    private async Task<Result<IReadOnlyList<TwitchModeratedChannel>>> ResolveChannelsAsync(
        Guid operatorChannelId,
        CancellationToken ct
    )
    {
        Result<IReadOnlyList<TwitchModeratedChannel>> moderated =
            await ResolveModeratedChannelsAsync(operatorChannelId, ct);
        if (moderated.IsFailure)
            return moderated;

        TwitchModeratedChannel? own = await _db
            .Channels.AsNoTracking()
            .Where(c => c.Id == operatorChannelId && c.TwitchChannelId != null)
            .Select(c => new TwitchModeratedChannel(c.TwitchChannelId!, c.Name, c.Name))
            .FirstOrDefaultAsync(ct);
        if (own is null || moderated.Value.Any(c => c.BroadcasterId == own.BroadcasterId))
            return moderated;

        return Result.Success<IReadOnlyList<TwitchModeratedChannel>>([own, .. moderated.Value]);
    }

    // Pages through every channel Twitch says the operator moderates. A first-page failure surfaces (e.g. the operator
    // token is missing user:read:moderated_channels); a later-page failure keeps what was already gathered.
    private async Task<Result<IReadOnlyList<TwitchModeratedChannel>>> ResolveModeratedChannelsAsync(
        Guid operatorChannelId,
        CancellationToken ct
    )
    {
        List<TwitchModeratedChannel> channels = [];
        string? cursor = null;
        do
        {
            Result<TwitchPage<TwitchModeratedChannel>> page =
                await _moderators.GetModeratedChannelsAsync(
                    operatorChannelId,
                    new(After: cursor),
                    ct
                );
            if (page.IsFailure)
            {
                if (channels.Count == 0)
                    return page.WithValue<IReadOnlyList<TwitchModeratedChannel>>(default!);
                break;
            }

            channels.AddRange(page.Value.Items);
            cursor = page.Value.NextCursor;
        } while (!string.IsNullOrEmpty(cursor));

        return Result.Success<IReadOnlyList<TwitchModeratedChannel>>(channels);
    }
}
