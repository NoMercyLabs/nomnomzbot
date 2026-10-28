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
