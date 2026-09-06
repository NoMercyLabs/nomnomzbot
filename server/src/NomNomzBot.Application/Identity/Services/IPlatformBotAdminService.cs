// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// The ADMIN-plane surface for the shared platform bot (S-BOT-PLATFORM-UI): once first-run setup is done,
/// this is the only route left to re-connect or replace it (the channel Integrations screen deliberately
/// polls the channel-scoped endpoint instead — the fix for the 2026-09-04 takeover). Every entry point gates
/// and audits through <see cref="Contracts.Authorization.IPlatformIamService.AuthorizePlatformAsync"/> under
/// <c>IamPermissionKeys.PlatformBotManage</c>, and the actual swap (<see cref="StartReconnectAsync"/> /
/// <see cref="PollReconnectAsync"/>) re-verifies the affected-channel count is still the one the operator was
/// shown — a stale count fails closed, the same law <c>NetworkBlockService</c> already applies.
/// </summary>
public interface IPlatformBotAdminService
{
    /// <summary>The real current state, read from the stored credential — never a hardcoded or assumed value.</summary>
    Task<Result<PlatformBotAdminStatusDto>> GetStatusAsync(
        Guid actingPrincipalId,
        CancellationToken cancellationToken = default
    );

    /// <summary>The counted blast radius a reconnect would touch. Must be rendered before one is armed.</summary>
    Task<Result<PlatformBotReconnectPreviewDto>> PreviewReconnectAsync(
        Guid actingPrincipalId,
        string justification,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Begins the reconnect device login. Rejects <c>PREVIEW_STALE</c> if the affected-channel count no
    /// longer matches the one <see cref="PreviewReconnectAsync"/> most recently reported.
    /// </summary>
    Task<Result<DeviceCodeStartDto>> StartReconnectAsync(
        Guid actingPrincipalId,
        string justification,
        int confirmedAffectedChannelCount,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Polls the reconnect device login once; on <c>authorized</c> the shared platform bot credential is
    /// REPLACED and every channel without its own custom bot resolves to the new account from then on.
    /// Re-checks the affected-channel count fresh — <c>PREVIEW_STALE</c> if it moved since the last preview.
    /// </summary>
    Task<Result<DeviceBotPollDto>> PollReconnectAsync(
        Guid actingPrincipalId,
        string deviceCode,
        string justification,
        int confirmedAffectedChannelCount,
        CancellationToken cancellationToken = default
    );
}
