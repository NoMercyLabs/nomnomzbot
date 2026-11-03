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

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Application.Games;
using NomNomzBot.Application.Games.Services;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Games.Pipeline;

/// <summary>
/// Supplies every live game the platform discovered for the <c>live_game_type</c> picker kind. The saved value is
/// the game key; the label is the game's display name and the secondary text is its player range. The catalogue is
/// the platform's own, so it holds no channel data.
/// </summary>
internal sealed class LiveGameTypeOptionProvider : IPipelineOptionProvider
{
    private readonly ILiveGameCatalog _catalog;

    public LiveGameTypeOptionProvider(ILiveGameCatalog catalog)
    {
        _catalog = catalog;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.LiveGameType;

    public Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            PipelineOptionPage.Of(_catalog.All.Select(g => ToOption(g.Key, g.Value)), request)
        );

    private static PipelineOption ToOption(string key, LiveGameManifest manifest) =>
        PipelineOptionPage.Option(key, manifest.DisplayName, PlayersOf(manifest));

    private static string PlayersOf(LiveGameManifest manifest) =>
        manifest is { MinPlayers: 1, MaxPlayers: 1 }
            ? "1 player"
            : $"{manifest.MinPlayers}-{manifest.MaxPlayers} players";
}
