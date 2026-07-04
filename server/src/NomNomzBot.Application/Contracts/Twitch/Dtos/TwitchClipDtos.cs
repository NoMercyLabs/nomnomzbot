// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "Clips" category wire models (POST /clips, GET /clips, POST /videos/clips, GET /clips/downloads).
// These records deserialize straight from Twitch's snake_case JSON via the transport's naming policy — no
// per-property annotations. Twitch ids stay strings (they are other users' / channels' / clips' ids); the
// owning tenant is always passed in as a Guid method argument, never here.

/// <summary>
/// The stub returned by a clip-create call (Create Clip, Create Clip From VOD): the new clip's id and the
/// edit URL the broadcaster opens to trim and publish it within the 15-second editing window.
/// </summary>
public sealed record TwitchClipStub(string Id, string EditUrl);

/// <summary>
/// Create Clip From VOD request fields. The broadcaster is the Guid method argument, resolved internally —
/// not part of this body. <c>EditorId</c> is the Twitch user id of the broadcaster or editor on whose behalf
/// the clip is created; <c>VodId</c> + <c>VodOffset</c> (seconds) locate the moment; <c>Title</c> is required;
/// <c>Duration</c> (seconds, 5–60) is optional and defaults to the platform default when omitted.
/// </summary>
public sealed record CreateClipFromVodRequest(
    string EditorId,
    string VodId,
    int VodOffset,
    string Title,
    double? Duration = null
);

/// <summary>
/// One clip's temporary download URLs (Get Clips Download). Twitch flags the links as short-lived; either
/// orientation URL is null when no video file exists for it.
/// </summary>
public sealed record TwitchClipDownload(
    string ClipId,
    string? LandscapeDownloadUrl,
    string? PortraitDownloadUrl
);

/// <summary>One video clip captured from a stream (Get Clips), with its metadata and view stats.</summary>
public sealed record TwitchClip(
    string Id,
    string Url,
    string EmbedUrl,
    string BroadcasterId,
    string BroadcasterName,
    string CreatorId,
    string CreatorName,
    string VideoId,
    string GameId,
    string Language,
    string Title,
    int ViewCount,
    DateTimeOffset CreatedAt,
    string ThumbnailUrl,
    double Duration,
    int? VodOffset,
    bool IsFeatured
);
