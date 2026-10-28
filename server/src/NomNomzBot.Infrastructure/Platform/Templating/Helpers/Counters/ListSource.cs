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

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PickLists.Dtos;
using NomNomzBot.Application.PickLists.Services;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Counters;

/// <summary>
/// One render's read access to the channel's pick lists. The provider is a singleton, so each read opens
/// its own scope for the scoped <see cref="IPickListService"/>. A missing list reads as an empty list,
/// the way the as-built <c>{list.pick.&lt;name&gt;}</c> treats it.
/// </summary>
internal sealed class ListSource(IServiceScopeFactory scopes, Guid? broadcasterId)
{
    /// <summary>False for a render with no channel: a named list cannot be read.</summary>
    public bool HasChannel => broadcasterId is not null;

    public async Task<IReadOnlyList<string>> ItemsAsync(string name, CancellationToken ct)
    {
        using IServiceScope scope = scopes.CreateScope();
        IPickListService lists = scope.ServiceProvider.GetRequiredService<IPickListService>();
        Result<PickListDto> found = await lists.GetByNameAsync(broadcasterId!.Value, name, ct);
        return found.IsSuccess ? found.Value.Items : [];
    }

    /// <summary>One uniformly random entry (the service's own pick); empty when the list is missing or empty.</summary>
    public async Task<string> PickAsync(string name, CancellationToken ct)
    {
        using IServiceScope scope = scopes.CreateScope();
        IPickListService lists = scope.ServiceProvider.GetRequiredService<IPickListService>();
        Result<string> pick = await lists.PickRandomAsync(broadcasterId!.Value, name, ct);
        return pick.IsSuccess ? pick.Value : string.Empty;
    }

    /// <summary>How many pick lists the channel has.</summary>
    public async Task<int> ListCountAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopes.CreateScope();
        IPickListService lists = scope.ServiceProvider.GetRequiredService<IPickListService>();
        Result<PagedList<PickListDto>> page = await lists.ListAsync(
            broadcasterId!.Value,
            new PickListSearch(null),
            new PaginationParams(PageSize: 1),
            ct
        );
        return page.IsSuccess ? page.Value.TotalCount : 0;
    }
}
