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
using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// The scoped services one engine command uses: the channel's provider and, for YouTube, the overlay player's
/// dispatcher. The engine opens a lease per command and disposes it after, so no scoped service (and no
/// DbContext) outlives the command. A lease built with <see cref="Of"/> holds no scope.
/// </summary>
public sealed class EngineLease(
    IMusicProvider provider,
    IYouTubePlayerDispatcher? dispatcher,
    IServiceScope? scope
) : IDisposable, IAsyncDisposable
{
    public IMusicProvider Provider => provider;

    public IYouTubePlayerDispatcher Dispatcher =>
        dispatcher
        ?? throw new InvalidOperationException("This lease carries no YouTube dispatcher.");

    public IMusicProviderStartTrack StartTrack => (IMusicProviderStartTrack)provider;

    public IMusicProviderPlayNow PlayNow => (IMusicProviderPlayNow)provider;

    public IMusicProviderConnectionGate ConnectionGate => (IMusicProviderConnectionGate)provider;

    public IMusicProviderPlayerGate PlayerGate => (IMusicProviderPlayerGate)provider;

    /// <summary>A lease over services that already exist and need no scope (fixed instances).</summary>
    public static EngineLease Of(
        IMusicProvider provider,
        IYouTubePlayerDispatcher? dispatcher = null
    ) => new(provider, dispatcher, null);

    public void Dispose() => scope?.Dispose();

    public ValueTask DisposeAsync()
    {
        if (scope is IAsyncDisposable asyncScope)
            return asyncScope.DisposeAsync();

        scope?.Dispose();

        return ValueTask.CompletedTask;
    }
}
