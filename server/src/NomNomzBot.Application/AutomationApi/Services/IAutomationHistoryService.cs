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

using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.AutomationApi.Services;

/// <summary>
/// Past events of the paired channel, in the same frame the live stream sends (scope <c>events</c>).
/// </summary>
public interface IAutomationHistoryService
{
    /// <summary>One page of events between <paramref name="from"/> and <paramref name="to"/>, oldest first.</summary>
    Task<Result<AutomationHistoryPage>> ReadAsync(
        AutomationPrincipal principal,
        DateTimeOffset from,
        DateTimeOffset to,
        string? cursor,
        int pageSize = 500,
        CancellationToken ct = default
    );
}
