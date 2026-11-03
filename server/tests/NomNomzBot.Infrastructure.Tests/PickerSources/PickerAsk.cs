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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.PickerSources;

/// <summary>The questions the step form asks a picker kind, and the checks every provider answers the same way.</summary>
internal static class PickerAsk
{
    public static readonly PaginationParams FirstPage = new(Page: 1, PageSize: 25);

    public static PipelineOptionQuery Of(
        string? search = null,
        string? value = null,
        PaginationParams? page = null
    ) => new(search, value, page ?? FirstPage);

    public static string[] Values(Result<PipelineOptionListResult> result) =>
        [.. result.Value.Items.Select(i => i.Value)];

    public static void AssertUnavailable(
        Result<PipelineOptionListResult> result,
        string expectedReason
    )
    {
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.SourceAvailable);
        Assert.Equal(expectedReason, result.Value.UnavailableReason);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }
}
