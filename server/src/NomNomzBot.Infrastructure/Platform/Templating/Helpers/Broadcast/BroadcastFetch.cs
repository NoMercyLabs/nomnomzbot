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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>
/// One service call in its own scope (the providers are singletons and the services are scoped). A service that
/// throws or answers with a failure reads as no data: a template render must never fail because one source is down.
/// </summary>
internal static class BroadcastFetch
{
    public static async Task<TValue?> RunAsync<TService, TValue>(
        IServiceScopeFactory scopes,
        Func<TService, CancellationToken, Task<Result<TValue>>> read,
        CancellationToken ct
    )
        where TService : notnull
        where TValue : class
    {
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
}

/// <summary>A value computed on the first ask and kept: a second ask in the same render gets the same task.</summary>
internal sealed class BroadcastMemo<T>(Func<CancellationToken, Task<T>> load)
{
    private readonly object _gate = new();
    private Task<T>? _task;

    public Task<T> GetAsync(CancellationToken ct)
    {
        lock (_gate)
            return _task ??= load(ct);
    }
}
