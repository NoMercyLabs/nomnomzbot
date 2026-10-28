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

using System.Text.Json;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Application.Abstractions.Pipeline;

/// <summary>
/// One pipeline action to run as the channel owner. <paramref name="Caller"/> names who asked (a widget name, a
/// script) and <paramref name="RateLimitPartition"/> is the bucket that caller's calls share.
/// </summary>
public sealed record OwnerActionRequest(
    Guid BroadcasterId,
    string ActionType,
    IReadOnlyDictionary<string, JsonElement>? Parameters,
    IReadOnlyDictionary<string, string>? Variables,
    string Caller,
    string RateLimitPartition
);

/// <summary>
/// Runs one registered pipeline action as the channel owner — the one executor behind both the widget invoke and
/// the script <c>actions.invoke</c> call. The only gate is the owner's own IAM (the same check the dashboard's
/// pipeline test-run passes), so a caller can do exactly what the owner can do, and nothing more.
/// </summary>
public interface IOwnerActionService
{
    /// <summary>
    /// Fails with <c>NOT_FOUND</c> for an unknown action type or channel, <c>FORBIDDEN</c> for an owner without
    /// the permission, and <c>RATE_LIMITED</c> for a caller that calls too often. A run that starts always
    /// returns an outcome, also when the action itself fails.
    /// </summary>
    Task<Result<WidgetActionOutcome>> RunAsync(
        OwnerActionRequest request,
        CancellationToken cancellationToken = default
    );
}
