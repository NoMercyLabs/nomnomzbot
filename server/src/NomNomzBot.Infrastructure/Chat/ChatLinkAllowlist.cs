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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Moderation.ChatFilters;
using NomNomzBot.Domain.Moderation.Enums;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Reads the channel's link allowlist from the same place the link filter keeps it: the allowed domains of every
/// enabled <c>LinkPolicy</c> chat filter, merged. The streamer edits it in the link filter, so chat HTML and the
/// link filter never disagree about which hosts are trusted.
/// </summary>
public sealed class ChatLinkAllowlist(IApplicationDbContext db) : IChatLinkAllowlist
{
    public async Task<Result<LinkPolicy>> GetAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        List<string?> policies = await db
            .ChatFilters.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f =>
                f.BroadcasterId == broadcasterId
                && f.DeletedAt == null
                && f.IsEnabled
                && f.FilterType == ChatFilterType.LinkPolicy
            )
            .Select(f => f.LinkPolicyJson)
            .ToListAsync(ct);

        List<string> domains =
        [
            .. policies
                .SelectMany(json => LinkPolicy.FromStoredJson(json).AllowedDomains)
                .Distinct(),
        ];
        return Result.Success(new LinkPolicy { AllowedDomains = domains });
    }
}
