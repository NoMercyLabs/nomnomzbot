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

using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Commands;

public sealed class CommandBindingGuard : ICommandBindingGuard
{
    private readonly IReadOnlyList<IBindingUsageSource> _sources;

    public CommandBindingGuard(IEnumerable<IBindingUsageSource> sources)
    {
        _sources = sources.ToList();
    }

    public async Task<IReadOnlyList<BindingUsage>> FindUsagesAsync(
        Guid broadcasterId,
        string commandTargetId,
        CancellationToken ct = default
    )
    {
        List<BindingUsage> usages = [];
        foreach (IBindingUsageSource source in _sources)
            usages.AddRange(await source.FindUsagesAsync(broadcasterId, commandTargetId, ct));
        return usages;
    }

    public async Task<Result> EnsureUnboundAsync(
        Guid broadcasterId,
        string commandTargetId,
        string commandName,
        CancellationToken ct = default
    )
    {
        IReadOnlyList<BindingUsage> usages = await FindUsagesAsync(
            broadcasterId,
            commandTargetId,
            ct
        );
        if (usages.Count == 0)
            return Result.Success();

        return Result.Failure(
            $"'{commandName}' is still used by {usages.Count} binding(s). Move or remove them first.",
            ICommandBindingGuard.InUseCode,
            errorData: new CommandInUse(commandName, usages)
        );
    }
}
