// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Community.Dtos;
using NomNomzBot.Application.Community.Services;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

/// <summary>The two reads the poll and the prediction helpers share: one service call, and the history.</summary>
internal static class PollPredictionFetch
{
    public const string PollKind = "poll";
    public const string PredictionKind = "prediction";

    /// <summary>
    /// One service call in its own scope. A missing channel or a service that throws reads as no data: a template
    /// render must never fail because one source is down.
    /// </summary>
    public static async Task<TValue?> RunAsync<TService, TValue>(
        IServiceScopeFactory scopes,
        Guid? broadcasterId,
        Func<TService, CancellationToken, Task<Result<TValue>>> read,
        CancellationToken ct
    )
        where TService : notnull
        where TValue : class
    {
        if (broadcasterId is null)
            return null;

        try
        {
            using IServiceScope scope = scopes.CreateScope();
            Result<TValue> result = await read(
                scope.ServiceProvider.GetRequiredService<TService>(),
                ct
            );
            return result.IsSuccess ? result.Value : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>The newest finished item of one kind from the journal-backed history (the history is newest first).</summary>
    public static async Task<PollPredictionHistoryItemDto?> LastEndedAsync(
        IServiceScopeFactory scopes,
        Guid? broadcasterId,
        string kind,
        CancellationToken ct
    ) =>
        (
            await RunAsync<
                IPollPredictionHistoryService,
                IReadOnlyList<PollPredictionHistoryItemDto>
            >(
                scopes,
                broadcasterId,
                (service, token) => service.ListAsync(broadcasterId!.Value, token),
                ct
            )
        )?.FirstOrDefault(item => item.Kind == kind && item.EndedAt is not null);
}
