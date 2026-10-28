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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Moderation.ChatFilters;

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// The channel's link allowlist: every host allowed by the channel's enabled link filters, merged into one
/// <see cref="LinkPolicy"/>. Chat HTML keeps a link to one of these hosts for every sender; a link elsewhere needs
/// the <c>chat:html:external-links</c> action.
/// </summary>
public interface IChatLinkAllowlist
{
    Task<Result<LinkPolicy>> GetAsync(Guid broadcasterId, CancellationToken ct = default);
}
