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
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Quotes;

/// <summary>
/// One channel's read access to its quotes. The provider is a singleton, so each read opens its own scope for
/// the scoped <see cref="IQuoteService"/>. A read that fails (no such quote, no quotes at all) is null.
/// </summary>
internal sealed class QuoteReader(IServiceScopeFactory scopes, Guid broadcasterId)
{
    public async Task<QuoteDto?> GetAsync(int number, CancellationToken ct)
    {
        using IServiceScope scope = scopes.CreateScope();
        Result<QuoteDto> found = await scope
            .ServiceProvider.GetRequiredService<IQuoteService>()
            .GetAsync(broadcasterId, number, ct);
        return found.IsSuccess ? found.Value : null;
    }

    public async Task<QuoteDto?> RandomAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopes.CreateScope();
        Result<QuoteDto> found = await scope
            .ServiceProvider.GetRequiredService<IQuoteService>()
            .GetRandomAsync(broadcasterId, ct);
        return found.IsSuccess ? found.Value : null;
    }

    /// <summary>How many quotes the channel has, or null when the list cannot be read.</summary>
    public async Task<int?> CountAsync(CancellationToken ct)
    {
        PagedList<QuoteDto>? page = await NewestPageAsync(ct);
        return page?.TotalCount;
    }

    /// <summary>The newest quote (the service lists newest first), or null when the channel has none.</summary>
    public async Task<QuoteDto?> LatestAsync(CancellationToken ct)
    {
        PagedList<QuoteDto>? page = await NewestPageAsync(ct);
        return page?.Items.FirstOrDefault();
    }

    private async Task<PagedList<QuoteDto>?> NewestPageAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopes.CreateScope();
        Result<PagedList<QuoteDto>> page = await scope
            .ServiceProvider.GetRequiredService<IQuoteService>()
            .ListAsync(broadcasterId, new QuoteSearch(null), new PaginationParams(PageSize: 1), ct);
        return page.IsSuccess ? page.Value : null;
    }
}
