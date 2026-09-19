// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.YouTube;

/// <summary>
/// Resolves a YouTube Super Sticker id (<c>snippet.superStickerDetails.superStickerMetadata.stickerId</c>)
/// to its real CDN image URL.
///
/// <para>
/// The <c>liveChatMessages</c> API never returns a sticker image URL — Google's own docs say so plainly
/// ("the image URL is not available using the API"). Google instead publishes a static reference CSV
/// mapping every sticker id to its image: <c>https://youtube.googleapis.com/super_stickers/sticker_ids_to_urls.csv</c>.
/// This fetches that CSV once and answers from memory afterwards, the same shape as
/// <c>ISevenTvPaintCatalogue</c> — the sticker set is small, static and changes rarely, so a lookup per
/// alert would be needless network chatter.
/// </para>
/// </summary>
public interface IYouTubeSuperStickerImageResolver
{
    /// <summary>
    /// The sticker's image URL, or null when the id is unknown or the catalogue could not be loaded. Never
    /// throws: a sticker image is decoration, and failing to resolve it must not cost the alert or block
    /// chat delivery — the caller degrades to text-only.
    /// </summary>
    Task<string?> ResolveAsync(string stickerId, CancellationToken cancellationToken = default);
}
