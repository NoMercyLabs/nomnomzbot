// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// !uptime — reports how long the stream has been live.
/// </summary>
public sealed class UptimeBuiltin : IBuiltinCommand
{
    private readonly IChannelRegistry _registry;

    public UptimeBuiltin(IChannelRegistry registry)
    {
        _registry = registry;
    }

    public string BuiltinKey => "uptime";
    public int DefaultCooldownSeconds => 30;
    public int DefaultMinPermissionLevel => 0;

    public Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        ChannelContext? ctx = _registry.Get(context.BroadcasterId);
        if (ctx is null || !ctx.IsLive)
            return Task.FromResult(Result.Success("The stream is currently offline."));

        string template =
            context.CustomResponseTemplate ?? "The stream is live! Check the dashboard for uptime.";

        return Task.FromResult(Result.Success(template));
    }
}
