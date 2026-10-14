// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Chat.Services;

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// Chat color service for catalog samples: the sample keeps the color its fixture carries and no color is
/// assigned, because a sample has no channel history to pick a least-used color from.
/// </summary>
internal sealed class SampleChatColors : IChatColorAssignmentService
{
    public Task<string?> ResolveAsync(
        Guid broadcasterId,
        string provider,
        string userId,
        string? lineColor,
        CancellationToken ct = default
    ) => Task.FromResult(lineColor);
}
