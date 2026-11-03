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
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Quotes.Pipeline;

/// <summary>
/// Supplies the asking channel's quotes for the <c>quote</c> picker kind. The saved value is the quote number as
/// text; the label is <c>#number</c> and the secondary text is the quote itself. The list keeps the service's order
/// (newest first), the search and the page go to the service, and a saved number is found by reading pages until it
/// turns up, so a quote past the first page still shows. A quote list that cannot be read is unavailable with the
/// service's reason.
/// </summary>
internal sealed class QuoteOptionProvider : IPipelineOptionProvider
{
    private readonly IQuoteService _quotes;

    public QuoteOptionProvider(IQuoteService quotes)
    {
        _quotes = quotes;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.Quote;

    public Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    ) =>
        request.IsValueLookup
            ? FindSavedAsync(broadcasterId, request.Value!, ct)
            : ListAsync(broadcasterId, request, ct);

    private async Task<Result<PipelineOptionListResult>> ListAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct
    )
    {
        string? term = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search;
        Result<PagedList<QuoteDto>> read = await _quotes.ListAsync(
            broadcasterId,
            new QuoteSearch(term),
            request.Pagination,
            ct
        );
        if (read.IsFailure)
            return PipelineOptionPage.Unavailable(read.ErrorMessage!);

        return Result.Success(
            PipelineOptionListResult.Of(
                [.. read.Value.Items.Select(ToOption)],
                read.Value.TotalCount
            )
        );
    }

    private async Task<Result<PipelineOptionListResult>> FindSavedAsync(
        Guid broadcasterId,
        string number,
        CancellationToken ct
    )
    {
        int page = 1;
        bool more = true;
        while (more)
        {
            Result<PagedList<QuoteDto>> read = await _quotes.ListAsync(
                broadcasterId,
                new QuoteSearch(null),
                new PaginationParams(page, PaginationParams.MaxPageSize),
                ct
            );
            if (read.IsFailure)
                return PipelineOptionPage.Unavailable(read.ErrorMessage!);

            QuoteDto? found = read.Value.Items.FirstOrDefault(q => NumberText(q) == number);
            if (found is not null)
                return Result.Success(PipelineOptionListResult.Of([ToOption(found)], 1));

            more = read.Value.HasNextPage;
            page++;
        }

        return PipelineOptionPage.Empty();
    }

    private static string NumberText(QuoteDto quote) => quote.Number.ToString();

    private static PipelineOption ToOption(QuoteDto quote) =>
        PipelineOptionPage.Option(NumberText(quote), $"#{quote.Number}", quote.Text);
}
