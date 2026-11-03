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
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Economy.Pipeline;

/// <summary>
/// Supplies the games the asking channel has configured for the <c>game_type</c> picker kind. The saved value is the
/// game type; the secondary text is the game's category, with ", disabled" added for a game the channel turned off.
/// A game list that cannot be read is unavailable, with the service's reason.
/// </summary>
internal sealed class GameTypeOptionProvider : IPipelineOptionProvider
{
    private readonly IGameService _games;

    public GameTypeOptionProvider(IGameService games)
    {
        _games = games;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.GameType;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        Result<IReadOnlyList<GameConfigDto>> games = await _games.ListGamesAsync(broadcasterId, ct);
        if (games.IsFailure)
            return PipelineOptionPage.Unavailable(games.ErrorMessage!);

        return PipelineOptionPage.Of(games.Value.Select(ToOption), request);
    }

    private static PipelineOption ToOption(GameConfigDto game) =>
        PipelineOptionPage.Option(
            game.GameType,
            game.GameType,
            game.IsEnabled ? game.Category : $"{game.Category}, disabled"
        );
}
