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
using NomNomzBot.Application.Vts.Dtos;

namespace NomNomzBot.Application.Vts.Services;

/// <summary>
/// The per-channel VTS transport (vtube-studio.md §3): sends one API request and returns the
/// response <c>data</c> JSON. Direct (self-host) dials the channel's endpoint and replays the stored
/// plugin token; bridge (SaaS) rides the OBS relay's leader. Failures are Result codes
/// (<c>VTS_DISABLED</c>, <c>VTS_UNAUTHORIZED</c>, <c>VTS_NOT_CONNECTED</c>, an APIError message).
/// </summary>
public interface IVtsTransport
{
    Task<Result<string>> RequestAsync(
        Guid broadcasterId,
        string requestType,
        string? dataJson,
        CancellationToken ct = default
    );
}

/// <summary>
/// The one-time plugin authorization (vtube-studio.md §0 D2): connect, send
/// <c>AuthenticationTokenRequest</c>, wait for the streamer to approve the in-VTS popup, and seal
/// the granted token onto the channel's connection row.
/// </summary>
public interface IVtsPluginAuthorizer
{
    Task<Result> AuthorizeAsync(Guid broadcasterId, CancellationToken ct = default);
}

/// <summary>Typed VTS control ops (vtube-studio.md §3) over <see cref="IVtsTransport"/>.</summary>
public interface IVtsControlService
{
    /// <summary>Generic pass-through: any VTS request type with a raw <c>data</c> payload.</summary>
    Task<Result<VtsRequestResult>> SendAsync(
        Guid broadcasterId,
        string requestType,
        string? payloadJson,
        CancellationToken ct = default
    );

    Task<Result> LoadModelAsync(Guid broadcasterId, string modelId, CancellationToken ct = default);

    Task<Result> TriggerHotkeyAsync(
        Guid broadcasterId,
        string hotkeyId,
        CancellationToken ct = default
    );

    Task<Result> SetExpressionAsync(
        Guid broadcasterId,
        string expressionFile,
        bool active,
        CancellationToken ct = default
    );

    Task<Result> MoveModelAsync(Guid broadcasterId, VtsMove move, CancellationToken ct = default);

    Task<Result> ColorTintAsync(
        Guid broadcasterId,
        VtsColorTint tint,
        CancellationToken ct = default
    );

    /// <summary>Models + current-model hotkeys + expressions, for the editor pickers.</summary>
    Task<Result<VtsModelInventory>> GetInventoryAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );
}
