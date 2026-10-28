// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
