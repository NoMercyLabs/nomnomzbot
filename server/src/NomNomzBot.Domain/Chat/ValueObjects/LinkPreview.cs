// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Chat.ValueObjects;

/// <summary>
/// An OpenGraph link preview for a url shared in chat (chat-decoration spec §4): the host plus the page's og:title,
/// og:description and og:image when present. Null on the fragment until the (opt-in, gated) link step resolves it;
/// any missing tag is simply null — the link still renders with its url.
/// </summary>
public sealed record LinkPreview(string Host, string? Title, string? Description, string? ImageUrl);
