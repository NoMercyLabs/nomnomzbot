// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
