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

namespace NomNomzBot.Domain.Chat.ValueObjects;

/// <summary>
/// An OpenGraph link preview for a url shared in chat (chat-decoration spec §4): the host plus the page's og:title,
/// og:description and og:image when present. Null on the fragment until the (opt-in, gated) link step resolves it;
/// any missing tag is simply null — the link still renders with its url.
/// </summary>
/// <param name="Host">The site the link points to, for example example.com.</param>
/// <param name="Title">The page title (og:title). Null when the page has none.</param>
/// <param name="Description">The page description (og:description). Null when the page has none.</param>
/// <param name="ImageUrl">The preview image link (og:image). Null when the page has none.</param>
public sealed record LinkPreview(string Host, string? Title, string? Description, string? ImageUrl);
