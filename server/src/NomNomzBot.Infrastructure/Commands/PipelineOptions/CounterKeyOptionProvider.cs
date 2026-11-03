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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Commands.PipelineOptions;

/// <summary>
/// Supplies the asking channel's named counters for the <c>counter_key</c> picker kind. The saved value is the
/// counter key; the secondary text is the value the counter holds now. The search and the page go to the counter
/// service. A saved key is found by an exact match on the key, so a key that no longer exists finds nothing and the
/// step form keeps showing the saved text. A counter list that cannot be read is unavailable with the service's reason.
/// </summary>
internal sealed class CounterKeyOptionProvider : IPipelineOptionProvider
{
    private readonly INamedCounterService _counters;

    public CounterKeyOptionProvider(INamedCounterService counters)
    {
        _counters = counters;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.CounterKey;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        string? term = request.IsValueLookup ? request.Value : request.Search;
        Result<PagedList<NamedCounterSummary>> read = await _counters.ListAsync(
            broadcasterId,
            term,
            request.IsValueLookup
                ? new PaginationParams(1, PaginationParams.MaxPageSize)
                : request.Pagination,
            ct
        );
        if (read.IsFailure)
            return PipelineOptionPage.Unavailable(read.ErrorMessage!);

        if (request.IsValueLookup)
        {
            List<PipelineOption> exact =
            [
                .. read
                    .Value.Items.Where(c =>
                        string.Equals(c.Key, request.Value, StringComparison.OrdinalIgnoreCase)
                    )
                    .Select(ToOption),
            ];
            return Result.Success(PipelineOptionListResult.Of(exact, exact.Count));
        }

        return Result.Success(
            PipelineOptionListResult.Of(
                [.. read.Value.Items.Select(ToOption)],
                read.Value.TotalCount
            )
        );
    }

    private static PipelineOption ToOption(NamedCounterSummary counter) =>
        PipelineOptionPage.Option(counter.Key, counter.Key, $"Value: {counter.Value}");
}
