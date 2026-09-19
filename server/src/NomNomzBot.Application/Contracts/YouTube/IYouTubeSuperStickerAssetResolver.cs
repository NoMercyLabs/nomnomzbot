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
/// Turns a YouTube Super Sticker id into OUR OWN channel asset — never the raw
/// <c>lh3.googleusercontent.com</c> CDN URL <see cref="IYouTubeSuperStickerImageResolver"/> resolves it to.
///
/// <para>
/// Stickers are just custom image assets — the same shape a Voice Trigger's <c>StickerAssetId</c> already
/// takes (<c>VoiceTriggerService.cs</c>: <c>IChannelAssetService.GetAsync</c> → <c>ChannelAssetDto.Url</c>,
/// rendered by <c>alerts.vue</c>'s generic <c>AlertCard.imageUrl</c>). On first seeing a given sticker id for
/// a channel, this downloads the CDN image once and stores it through the SAME asset-upload path an
/// operator's own upload takes, so it inherits that path's content sniffing, size caps and per-channel quota
/// — then answers with THAT asset's stable serving URL. A repeat of the same sticker for the same channel
/// answers from a cache instead of downloading or uploading again.
/// </para>
/// </summary>
public interface IYouTubeSuperStickerAssetResolver
{
    /// <summary>
    /// The channel asset URL for this sticker, or null when the sticker id is unknown or any step of the
    /// resolve/download/upload chain failed. Never throws: a sticker image is decoration, and failing to
    /// resolve it must not cost the alert or block chat delivery — the caller degrades to text-only.
    /// </summary>
    Task<string?> ResolveAssetUrlAsync(
        Guid broadcasterId,
        string stickerId,
        CancellationToken cancellationToken = default
    );
}
