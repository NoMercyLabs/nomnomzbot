// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// Builds the Spotify and YouTube engines of a channel. The factory is a singleton; the providers and the
/// dispatcher are scoped, so each engine command resolves them in a scope of its own (see
/// <see cref="EngineLease"/>) and the factory holds none of them.
/// The director sends engine commands on its own task, with no user action in flight, so the lease also
/// claims the channel's configured playback basis for as long as the command runs. That is the one place the
/// engine writes get their sanction.
/// </summary>
public sealed class PlaybackEngineFactory(
    IServiceScopeFactory scopes,
    IYouTubePlayerStateStore store,
    IOutboundSanctionAccessor sanctions
) : IPlaybackEngineFactory
{
    private const string PlaybackBasis = "music:playback_engine";
    private const string SpotifyKey = "spotify";
    private const string YouTubeKey = "youtube";

    public IReadOnlyList<IPlaybackEngine> Create(Guid broadcasterId) =>
        [
            new SpotifyPlaybackEngine(broadcasterId, () => Open(SpotifyKey, false)),
            new YouTubePlaybackEngine(broadcasterId, () => Open(YouTubeKey, true), store),
        ];

    /// <summary>
    /// A user action already in force (a viewer's or the owner's skip) stays the recorded basis; only a
    /// director-driven command with no sanction in force claims the channel's playback configuration.
    /// </summary>
    private IDisposable? ClaimPlaybackBasis() =>
        sanctions.Current is not null
            ? null
            : sanctions.Begin(OutboundSanction.ChannelConfiguration(PlaybackBasis));

    private EngineLease Open(string providerKey, bool withDispatcher)
    {
        AsyncServiceScope scope = scopes.CreateAsyncScope();
        try
        {
            IMusicProvider provider = scope
                .ServiceProvider.GetServices<IMusicProvider>()
                .Single(candidate => candidate.Provider == providerKey);
            IYouTubePlayerDispatcher? dispatcher = withDispatcher
                ? scope.ServiceProvider.GetRequiredService<IYouTubePlayerDispatcher>()
                : null;

            return new(provider, dispatcher, scope, ClaimPlaybackBasis());
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
