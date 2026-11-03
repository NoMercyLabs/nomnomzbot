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
using NomNomzBot.Application.Giveaways.Dtos;
using NomNomzBot.Application.Giveaways.Services;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Giveaways.Pipeline;

/// <summary>
/// Supplies the asking channel's giveaways for the <c>giveaway</c> picker kind. The saved value is the giveaway id;
/// the label is the title and the secondary text is the status. It reads every page of the service, a saved id in
/// the 26-character wire form still finds its giveaway, and a list that cannot be read is unavailable with the
/// service's reason.
/// </summary>
internal sealed class GiveawayOptionProvider : IPipelineOptionProvider
{
    private readonly IGiveawayService _giveaways;

    public GiveawayOptionProvider(IGiveawayService giveaways)
    {
        _giveaways = giveaways;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.Giveaway;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        List<GiveawayDto> items = [];
        int page = 1;
        bool more = true;
        while (more)
        {
            Result<PagedList<GiveawayDto>> read = await _giveaways.ListAsync(
                broadcasterId,
                new GiveawayFilter(),
                new PaginationParams(page, PaginationParams.MaxPageSize),
                ct
            );
            if (read.IsFailure)
                return PipelineOptionPage.Unavailable(read.ErrorMessage!);

            items.AddRange(read.Value.Items);
            more = read.Value.HasNextPage;
            page++;
        }

        return PipelineOptionPage.Of(
            items.Select(g => PipelineOptionPage.Option(g.Id.ToString(), g.Title, g.Status)),
            PipelineOptionPage.WithOwnedIdValue(request)
        );
    }
}
