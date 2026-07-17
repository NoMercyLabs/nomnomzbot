// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.AutomationApi.Services;

/// <summary>
/// The data-plane command surface (automation-api.md §3/§4.1). Every method enforces the principal's
/// scopes (and the invoke allowlist) plus the per-token rate limit BEFORE acting — a denied call has
/// no side effect.
/// </summary>
public interface IAutomationCommandService
{
    /// <summary>Fire-and-forget pipeline run attributed to the token (scope <c>invoke</c> + allowlist).</summary>
    Task<Result<AutomationInvokeResult>> InvokePipelineAsync(
        AutomationPrincipal principal,
        AutomationInvokeRequest request,
        CancellationToken ct = default
    );

    /// <summary>The pipelines the token may see (scope <c>read</c>) — enabled ones, honoring the allowlist.</summary>
    Task<Result<IReadOnlyList<AutomationPipelineRef>>> ListPipelinesAsync(
        AutomationPrincipal principal,
        CancellationToken ct = default
    );

    /// <summary>The channel's enabled chat commands (scope <c>read</c>).</summary>
    Task<Result<IReadOnlyList<AutomationCommandRef>>> ListCommandsAsync(
        AutomationPrincipal principal,
        CancellationToken ct = default
    );

    /// <summary>Broadcaster + instance summary (scope <c>read</c>).</summary>
    Task<Result<AutomationInfo>> GetInfoAsync(
        AutomationPrincipal principal,
        CancellationToken ct = default
    );

    /// <summary>Send a message / reply / whisper as the bot (scope <c>chat</c>).</summary>
    Task<Result> SendChatAsync(
        AutomationPrincipal principal,
        AutomationChatRequest request,
        CancellationToken ct = default
    );
}
