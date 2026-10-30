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

using NomNomzBot.Application.Admin.Diagnostics;
using NomNomzBot.Application.Admin.Dtos;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Admin.Diagnostics;

/// <summary>The <c>runs</c> section: one page of the channel's pipeline runs, read through the same
/// <see cref="IPipelineExecutionQueryService"/> and with the same filters as the channel's own run history.</summary>
public sealed class ChannelRunsDiagnosticSection(IPipelineExecutionQueryService runs)
    : IChannelDiagnosticSection
{
    public string Key => "runs";

    public async Task<Result> ReadIntoAsync(
        ChannelDiagnosticRequest request,
        ChannelDiagnosticsDto diagnostics,
        CancellationToken cancellationToken = default
    )
    {
        Result<PagedList<PipelineExecutionSummaryDto>> page = await runs.ListAsync(
            request.BroadcasterId.ToString(),
            request.RunPage,
            request.FailedRunsOnly,
            request.PipelineId,
            cancellationToken
        );
        if (page.IsFailure)
            return page;

        diagnostics.Runs = new(
            page.Value.Items,
            page.Value.Page,
            page.Value.PageSize,
            page.Value.TotalCount,
            page.Value.HasNextPage
        );
        return Result.Success();
    }
}
