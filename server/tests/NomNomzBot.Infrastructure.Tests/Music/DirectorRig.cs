// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.Playback;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The playback director a <see cref="MusicService"/> test sends its requests to: the director registry over
/// the SAME queue store the service uses, with fake engines a test can script before the first request and a
/// bus that records what the director published. One rig belongs to one queue store.
/// </summary>
internal sealed class DirectorRig
{
    private static readonly ConditionalWeakTable<ISongRequestQueueStore, DirectorRig> Rigs = new();

    private DirectorRig(ISongRequestQueueStore store)
    {
        Engines = new();
        Bus = new();
        Registry = new(store, Engines, Bus, TimeProvider.System);
    }

    public ScriptableEngineFactory Engines { get; }

    /// <summary>Attaches the output and reports a playing Spotify track, so the director is Playing on Spotify.</summary>
    public async Task PlaySpotifyAsync(Guid broadcasterId)
    {
        await Registry.SendAsync(broadcasterId, new OutputAttached());
        await Registry.SendAsync(
            broadcasterId,
            new EngineReport(EngineKind.Spotify, "spotify:track:x", true, 1_000, 200_000)
        );
    }

    public RecordingBus Bus { get; }

    public PlaybackDirectorRegistry Registry { get; }

    /// <summary>Makes the queue store a service is built over, and hands back the same store.</summary>
    public static SongRequestQueueStore NewStore(out SongRequestQueueStore store)
    {
        store = new();
        return store;
    }

    /// <summary>The rig that belongs to <paramref name="store"/>, made on first use.</summary>
    public static DirectorRig Of(ISongRequestQueueStore store) =>
        Rigs.GetValue(store, key => new(key));

    /// <summary>Hands out one engine pair per channel, built the first time a test or the registry asks.</summary>
    public sealed class ScriptableEngineFactory : IPlaybackEngineFactory
    {
        private readonly Dictionary<Guid, FakeEngine[]> _pairs = [];
        private readonly Dictionary<Guid, IPlaybackEngine> _realSpotify = [];

        public FakeEngine EngineOf(Guid broadcasterId, EngineKind kind) =>
            PairOf(broadcasterId).Single(engine => engine.Kind == kind);

        /// <summary>
        /// Puts the real Spotify engine, over <paramref name="provider"/>, in place of the fake one for a channel,
        /// so a test can drive a refused start from the provider's own HTTP answer instead of scripting it.
        /// </summary>
        public void UseRealSpotify(Guid broadcasterId, IMusicProvider provider)
        {
            lock (_pairs)
                _realSpotify[broadcasterId] = new SpotifyPlaybackEngine(
                    broadcasterId,
                    () => EngineLease.Of(provider)
                );
        }

        public IReadOnlyList<IPlaybackEngine> Create(Guid broadcasterId)
        {
            FakeEngine[] pair = PairOf(broadcasterId);
            lock (_pairs)
                return _realSpotify.TryGetValue(broadcasterId, out IPlaybackEngine? real)
                    ? [real, pair.Single(engine => engine.Kind == EngineKind.YouTube)]
                    : pair;
        }

        private FakeEngine[] PairOf(Guid broadcasterId)
        {
            lock (_pairs)
            {
                if (!_pairs.TryGetValue(broadcasterId, out FakeEngine[]? pair))
                    _pairs[broadcasterId] = pair = [
                        new(EngineKind.Spotify),
                        new(EngineKind.YouTube),
                    ];

                return pair;
            }
        }
    }
}
