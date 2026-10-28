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

namespace NomNomzBot.Infrastructure.Chat.YouTube;

/// <summary>
/// The named <see cref="HttpClient"/> <see cref="YouTubeSuperStickerImageResolver"/> fetches Google's static
/// sticker-id-to-image-URL CSV through. A fixed public reference file, not user-supplied input, so this is a
/// plain client — same shape as <c>ChatEmoteHttpClient</c>.
/// </summary>
internal static class YouTubeSuperStickerHttpClient
{
    public const string Name = "youtube-super-stickers";
}
