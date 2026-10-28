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

using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Analytics;

/// <summary>
/// Folds each <c>CommandExecutedEvent</c> (custom and built-in, the one command-execution fact) into one
/// <see cref="CommandUsage"/> row for the caller in the channel — the per-viewer record the viewer page reads
/// for "last used". Idempotent on (channel, viewer, command name, time of the run).
/// </summary>
public sealed class CommandUsageProjection(IApplicationDbContext db, ViewerResolver resolver)
    : IProjection
{
    private static readonly HashSet<string> Subscribed = new(StringComparer.Ordinal)
    {
        "CommandExecutedEvent",
    };

    public string Name => "command-usage";
    public bool IsGlobal => false;
    public IReadOnlySet<string> SubscribedEventTypes => Subscribed;

    public async Task<Result> ApplyAsync(
        EventRecord @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId is not { } broadcasterId)
            return Result.Success();

        JObject? payload = ViewerResolver.TryParse(@event.PayloadJson);
        string? commandName = payload?["CommandName"]?.Value<string>();
        if (payload is null || string.IsNullOrEmpty(commandName))
            return Result.Success();

        (string Provider, string ExternalUserId, string Login, string Display)? identity =
            ViewerResolver.ParseIdentity(payload);
        if (identity is null)
            return Result.Success();

        ViewerProfile? profile = await resolver.ResolveAsync(
            broadcasterId,
            identity.Value.Provider,
            identity.Value.ExternalUserId,
            identity.Value.Login,
            identity.Value.Display,
            cancellationToken
        );
        if (profile is null)
            return Result.Success();

        bool alreadyFolded = await db.CommandUsages.AnyAsync(
            u =>
                u.BroadcasterId == broadcasterId
                && u.ViewerUserId == profile.ViewerUserId
                && u.CommandNameSnapshot == commandName
                && u.CreatedAt == @event.OccurredAt,
            cancellationToken
        );
        if (alreadyFolded)
            return Result.Success();

        string normalized = commandName.ToLowerInvariant();
        Guid? commandId = await db
            .Commands.Where(c => c.BroadcasterId == broadcasterId && c.NameNormalized == normalized)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        db.CommandUsages.Add(
            new CommandUsage
            {
                BroadcasterId = broadcasterId,
                CommandId = commandId,
                CommandNameSnapshot = commandName,
                ViewerProfileId = profile.Id,
                ViewerUserId = profile.ViewerUserId,
                WasSuccessful = payload["Succeeded"]?.Value<bool?>() == true,
                CreatedAt = @event.OccurredAt,
            }
        );
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResetAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<CommandUsage> rows = await (
            broadcasterId is { } id
                ? db.CommandUsages.Where(u => u.BroadcasterId == id)
                : db.CommandUsages
        ).ToListAsync(cancellationToken);

        db.CommandUsages.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
