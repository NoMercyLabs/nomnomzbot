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

using NomNomzBot.Application.Admin.Dtos;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Admin.Diagnostics;

/// <summary>
/// One part of a channel's admin diagnostics (summary, runs, ...), asked for by <see cref="Key"/> in
/// <c>admin/channels/{broadcasterId}/diagnostics?sections=</c>. A new section is a new class plus a property on
/// <see cref="ChannelDiagnosticsDto"/>; it never adds an endpoint.
/// </summary>
public interface IChannelDiagnosticSection
{
    /// <summary>The key a caller passes in <c>sections</c>, and the name of the property it fills.</summary>
    string Key { get; }

    /// <summary>Reads the section for <paramref name="request"/> and writes it onto <paramref name="diagnostics"/>.</summary>
    Task<Result> ReadIntoAsync(
        ChannelDiagnosticRequest request,
        ChannelDiagnosticsDto diagnostics,
        CancellationToken cancellationToken = default
    );
}
