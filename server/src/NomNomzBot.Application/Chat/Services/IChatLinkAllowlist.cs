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
