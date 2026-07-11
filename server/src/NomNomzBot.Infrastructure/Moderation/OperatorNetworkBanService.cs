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
    private readonly ILogger<OperatorNetworkBanService> _logger;

    public OperatorNetworkBanService(
        IChannelAccessService channelAccess,
        ITwitchModeratorsApi moderators,
        ITwitchModerationApi moderation,
        ILogger<OperatorNetworkBanService> logger
    )
    {
        _channelAccess = channelAccess;
        _moderators = moderators;
        _moderation = moderation;
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

        Result<IReadOnlyList<TwitchModeratedChannel>> channels =
            await ResolveModeratedChannelsAsync(operatorChannelId, ct);
        if (channels.IsFailure)
            return channels.WithValue<NetworkBanResult>(default!);

        List<ChannelBanOutcome> outcomes = new(channels.Value.Count);
        foreach (TwitchModeratedChannel channel in channels.Value)
        {
            Result outcome = await perChannel(channel);

            outcomes.Add(
                new ChannelBanOutcome(
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
                    new TwitchPageRequest(After: cursor),
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
