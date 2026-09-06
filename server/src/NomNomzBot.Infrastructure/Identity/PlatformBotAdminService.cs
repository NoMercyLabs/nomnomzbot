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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// See <see cref="IPlatformBotAdminService"/>. Composes the existing <see cref="IAuthService"/> bot-device
/// flow rather than duplicating it — the only new logic here is the admin gate/audit wrapper, the
/// three-state read (<see cref="IPlatformBotReadinessGate"/> for real token usability), and the counted
/// blast radius (every channel with no active <see cref="ChannelBotAuthorization"/> of its own).
/// </summary>
public sealed class PlatformBotAdminService(
    IApplicationDbContext db,
    IAuthService authService,
    IPlatformBotReadinessGate readiness,
    IPlatformIamService iam
) : IPlatformBotAdminService
{
    public async Task<Result<PlatformBotAdminStatusDto>> GetStatusAsync(
        Guid actingPrincipalId,
        CancellationToken cancellationToken = default
    )
    {
        Result gate = await RequireAsync(actingPrincipalId, null, "status", cancellationToken);
        if (gate.IsFailure)
            return gate.WithValue<PlatformBotAdminStatusDto>(null!);

        BotAccount? bot = await db
            .BotAccounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                b =>
                    b.IdentityType == AuthEnums.BotIdentityType.Shared
                    && b.IsActive
                    && b.DeletedAt == null,
                cancellationToken
            );
        if (bot is null)
            return Result.Success(
                new PlatformBotAdminStatusDto(PlatformBotConnectionState.NeverConnected, null, null)
            );

        bool usable = await readiness.IsPlatformBotConfiguredAsync(cancellationToken);
        return Result.Success(
            new PlatformBotAdminStatusDto(
                usable
                    ? PlatformBotConnectionState.ConnectedAndWorking
                    : PlatformBotConnectionState.ConnectedTokenUnusable,
                bot.BotUsername,
                bot.BotUsername
            )
        );
    }

    public async Task<Result<PlatformBotReconnectPreviewDto>> PreviewReconnectAsync(
        Guid actingPrincipalId,
        string justification,
        CancellationToken cancellationToken = default
    )
    {
        Result gate = await RequireAsync(
            actingPrincipalId,
            justification,
            "reconnect:preview",
            cancellationToken
        );
        if (gate.IsFailure)
            return gate.WithValue<PlatformBotReconnectPreviewDto>(null!);

        int affected = await CountAffectedChannelsAsync(cancellationToken);
        return Result.Success(new PlatformBotReconnectPreviewDto(affected));
    }

    public async Task<Result<DeviceCodeStartDto>> StartReconnectAsync(
        Guid actingPrincipalId,
        string justification,
        int confirmedAffectedChannelCount,
        CancellationToken cancellationToken = default
    )
    {
        Result gate = await RequireAsync(
            actingPrincipalId,
            justification,
            "reconnect:start",
            cancellationToken
        );
        if (gate.IsFailure)
            return gate.WithValue<DeviceCodeStartDto>(null!);

        Result stale = await RequireFreshCountAsync(
            confirmedAffectedChannelCount,
            cancellationToken
        );
        if (stale.IsFailure)
            return stale.WithValue<DeviceCodeStartDto>(null!);

        return await authService.StartBotDeviceLoginAsync(cancellationToken);
    }

    public async Task<Result<DeviceBotPollDto>> PollReconnectAsync(
        Guid actingPrincipalId,
        string deviceCode,
        string justification,
        int confirmedAffectedChannelCount,
        CancellationToken cancellationToken = default
    )
    {
        Result gate = await RequireAsync(
            actingPrincipalId,
            justification,
            "reconnect:poll",
            cancellationToken
        );
        if (gate.IsFailure)
            return gate.WithValue<DeviceBotPollDto>(null!);

        Result stale = await RequireFreshCountAsync(
            confirmedAffectedChannelCount,
            cancellationToken
        );
        if (stale.IsFailure)
            return stale.WithValue<DeviceBotPollDto>(null!);

        return await authService.PollBotDeviceLoginAsync(deviceCode, cancellationToken);
    }

    private async Task<Result> RequireFreshCountAsync(
        int confirmedAffectedChannelCount,
        CancellationToken cancellationToken
    )
    {
        int current = await CountAffectedChannelsAsync(cancellationToken);
        return current == confirmedAffectedChannelCount
            ? Result.Success()
            : Result.Failure(
                "The affected-channel count changed since the last preview. Run preview again.",
                "PREVIEW_STALE"
            );
    }

    /// <summary>Every channel with no active custom bot of its own — the set that speaks through the shared bot.</summary>
    private async Task<int> CountAffectedChannelsAsync(CancellationToken cancellationToken)
    {
        int total = await db.Channels.CountAsync(cancellationToken);
        int withOwnBot = await db
            .ChannelBotAuthorizations.IgnoreQueryFilters()
            .Where(a => a.IsActive && a.DeletedAt == null)
            .Select(a => a.BroadcasterId)
            .Distinct()
            .CountAsync(cancellationToken);
        return Math.Max(0, total - withOwnBot);
    }

    private async Task<Result> RequireAsync(
        Guid actingPrincipalId,
        string? justification,
        string targetResource,
        CancellationToken cancellationToken
    )
    {
        Result<bool> allowed = await iam.AuthorizePlatformAsync(
            actingPrincipalId,
            IamPermissionKeys.PlatformBotManage,
            null,
            false,
            justification,
            cancellationToken,
            targetResource
        );
        if (allowed.IsFailure)
            return allowed;
        return allowed.Value
            ? Result.Success()
            : Result.Failure($"Requires {IamPermissionKeys.PlatformBotManage}.", "FORBIDDEN");
    }
}
