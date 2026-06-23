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
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// Fetches an OpenGraph preview for a url shared in chat (chat-decoration spec §3.5) through the SSRF-hardened
/// egress-allowlisted client, caching the result per url. Best-effort: a non-html response, a blocked/failed fetch,
/// or a page with no OpenGraph tags yields a null preview rather than an error.
/// </summary>
public interface ILinkPreviewService
{
    Task<Result<LinkPreview?>> FetchAsync(Uri url, CancellationToken ct = default);
}
