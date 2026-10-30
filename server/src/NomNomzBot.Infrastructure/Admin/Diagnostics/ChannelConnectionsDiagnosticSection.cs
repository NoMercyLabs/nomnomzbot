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
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Admin.Diagnostics;

/// <summary>
/// The <c>connections</c> section: one health row for every connection the channel has, gathered from every
/// registered <see cref="IChannelConnectionSource"/> and ordered by kind, provider and account so the list is
/// stable. A provider the channel never connected has no row.
/// </summary>
public sealed class ChannelConnectionsDiagnosticSection(
    IEnumerable<IChannelConnectionSource> sources
) : IChannelDiagnosticSection
{
    public string Key => "connections";

    public async Task<Result> ReadIntoAsync(
        ChannelDiagnosticRequest request,
        ChannelDiagnosticsDto diagnostics,
        CancellationToken cancellationToken = default
    )
    {
        List<ChannelDiagnosticConnectionDto> rows = [];
        foreach (IChannelConnectionSource source in sources)
            rows.AddRange(await source.ReadAsync(request.BroadcasterId, cancellationToken));

        diagnostics.Connections =
        [
            .. rows.OrderBy(r => r.Kind, StringComparer.Ordinal)
                .ThenBy(r => r.Provider, StringComparer.Ordinal)
                .ThenBy(r => r.AccountName, StringComparer.Ordinal),
        ];
        return Result.Success();
    }
}
