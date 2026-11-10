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

namespace NomNomzBot.Application.Contracts.YouTube;

/// <summary>
/// Puts one of the channel's Twitch clips on the streamer's own YouTube channel (<c>videos.insert</c>, with
/// the streamer's YouTube grant). Google allows this app 100 uploads a day in total, and has no call that
/// reads what is left, so the bot counts its own uploads per Google day.
/// </summary>
public interface IYouTubeClipUploadService
{
    /// <summary>
    /// Sends the clip's video to YouTube. A channel that is not connected, a clip Twitch gives no download
    /// for, a refused grant or a used-up day are normal states: the answer names which one in
    /// <see cref="YouTubeClipUploadDto.Outcome"/>. A title, description or privacy YouTube would refuse is a
    /// <c>VALIDATION_FAILED</c> failure.
    /// </summary>
    Task<Result<YouTubeClipUploadDto>> UploadAsync(
        Guid broadcasterId,
        string clipId,
        YouTubeClipUploadRequest request,
        CancellationToken ct = default
    );

    /// <summary>How many uploads this app has left in the current Google day.</summary>
    Task<int> UploadsLeftTodayAsync(CancellationToken ct = default);
}

/// <summary>What happened to an upload. Text values, so the dashboard reads the word.</summary>
public static class YouTubeClipUploadOutcome
{
    /// <summary>The video is on YouTube.</summary>
    public const string Uploaded = "uploaded";

    /// <summary>The channel has no YouTube connection.</summary>
    public const string NotConnected = "not_connected";

    /// <summary>YouTube refused the streamer's grant for uploads.</summary>
    public const string NotAllowed = "not_allowed";

    /// <summary>Twitch gave no download for the clip (not this channel's clip, gone, or no permission).</summary>
    public const string ClipUnavailable = "clip_unavailable";

    /// <summary>The app's or the channel's upload limit for today is used up.</summary>
    public const string LimitReached = "limit_reached";
}

/// <summary>The privacy a YouTube video can have.</summary>
public static class YouTubeVideoPrivacy
{
    public const string Private = "private";
    public const string Unlisted = "unlisted";
    public const string Public = "public";

    public static readonly IReadOnlyList<string> All = [Private, Unlisted, Public];
}

/// <summary>
/// The words the video gets. <paramref name="Privacy"/> is one of <see cref="YouTubeVideoPrivacy"/>; left
/// out, the video is private.
/// </summary>
public sealed record YouTubeClipUploadRequest(string Title, string? Description, string? Privacy);

/// <summary>
/// The result of an upload. <paramref name="VideoId"/> and <paramref name="Url"/> are set only when
/// <paramref name="Outcome"/> is <see cref="YouTubeClipUploadOutcome.Uploaded"/>.
/// </summary>
public sealed record YouTubeClipUploadDto(
    string Outcome,
    string? VideoId,
    string? Url,
    int UploadsLeftToday
);
