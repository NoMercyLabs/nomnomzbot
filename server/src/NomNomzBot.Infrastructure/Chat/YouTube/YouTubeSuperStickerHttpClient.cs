// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
