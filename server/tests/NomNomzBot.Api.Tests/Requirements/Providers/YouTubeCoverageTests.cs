// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Domain.Music.Interfaces;
using Xunit;

namespace NomNomzBot.Api.Tests.Requirements.Providers;

/// <summary>
/// REQUIREMENT: the YouTube integration must expose the YouTube Data API surface the bot uses — live-chat
/// read/write/moderation (<see cref="IYouTubeLiveChatClient"/>) plus search and playlists via the music seam.
/// HARD project rule (external-api-full-management-coverage): whatever the YouTube Data API lets the bot do, the
/// bot must let the operator manage. These tests enumerate what EXISTS (reflection) and compare it to the
/// YouTube Data API capability set. A red is a YouTube capability the bot does not yet expose.
/// </summary>
public sealed class YouTubeCoverageTests
{
    [Fact]
    public void YouTube_live_chat_client_covers_the_documented_live_chat_surface()
    {
        HashSet<string> methods = ProviderSurface.MethodNames(typeof(IYouTubeLiveChatClient));

        (string Capability, string[] Keywords)[] expected =
        [
            ("Resolve active broadcast live chat (liveBroadcasts.list)", ["GetActiveLiveChat"]),
            ("Read live-chat messages (liveChatMessages.list)", ["ListMessages"]),
            ("Send a live-chat message (liveChatMessages.insert)", ["SendMessage"]),
            ("Ban/timeout a viewer (liveChatBans.insert)", ["BanUser"]),
            ("Lift a ban (liveChatBans.delete)", ["UnbanUser"]),
            ("Delete a message (liveChatMessages.delete)", ["DeleteMessage"]),
            ("Resolve own channel (channels.list?mine=true)", ["GetOwnChannel"]),
            (
                "Update active broadcast title (liveBroadcasts.update)",
                ["UpdateActiveBroadcastTitle"]
            ),
        ];

        List<string> missing = expected
            .Where(capability => !methods.Covers(capability.Keywords))
            .Select(capability => capability.Capability)
            .ToList();

        missing
            .Should()
            .BeEmpty("the bot must expose the YouTube live-chat read/write/moderation surface");
    }

    [Fact]
    public void YouTube_music_seam_covers_search_and_playlist_management()
    {
        HashSet<string> methods = ProviderSurface.MethodNames(
            typeof(IMusicProvider),
            typeof(IMusicRemoteProvider),
            typeof(IMusicProviderManageApi)
        );

        (string Capability, string[] Keywords)[] expected =
        [
            ("Search (search.list)", ["Search"]),
            ("List playlists (playlists.list)", ["ListPlaylists", "GetPlaylists"]),
            ("Create playlist (playlists.insert)", ["CreatePlaylist"]),
            ("Update playlist (playlists.update)", ["UpdatePlaylist"]),
            ("Delete playlist (playlists.delete)", ["DeletePlaylist"]),
            ("Add playlist items (playlistItems.insert)", ["AddPlaylistTracks"]),
            ("Remove playlist items (playlistItems.delete)", ["RemovePlaylistTracks"]),
            ("Rate a video (videos.rate)", ["RateTrack", "SaveTracks"]),
            ("Subscribe to a channel (subscriptions.insert)", ["Follow"]),
            ("Unsubscribe from a channel (subscriptions.delete)", ["Unfollow"]),
        ];

        List<string> missing = expected
            .Where(capability => !methods.Covers(capability.Keywords))
            .Select(capability => capability.Capability)
            .ToList();

        missing
            .Should()
            .BeEmpty(
                "the bot must expose YouTube search + playlist/subscription management via the music seam"
            );
    }

    [Fact]
    public void YouTube_live_chat_client_covers_the_full_moderation_surface()
    {
        HashSet<string> methods = ProviderSurface.MethodNames(typeof(IYouTubeLiveChatClient));

        // YouTube Data API live-chat moderator management the bot does not yet expose — the concrete backlog.
        (string Capability, string[] Keywords)[] expected =
        [
            (
                "List live-chat moderators (liveChatModerators.list)",
                ["GetLiveChatModerators", "ListModerators"]
            ),
            (
                "Add a live-chat moderator (liveChatModerators.insert)",
                ["AddLiveChatModerator", "AddModerator"]
            ),
            (
                "Remove a live-chat moderator (liveChatModerators.delete)",
                ["RemoveLiveChatModerator", "RemoveModerator"]
            ),
        ];

        List<string> missing = expected
            .Where(capability => !methods.Covers(capability.Keywords))
            .Select(capability => capability.Capability)
            .ToList();

        missing
            .Should()
            .BeEmpty(
                "the YouTube Data API lets the bot manage live-chat moderators; full-management-coverage requires it — "
                    + $"missing: [{string.Join(", ", missing)}]"
            );
    }
}
