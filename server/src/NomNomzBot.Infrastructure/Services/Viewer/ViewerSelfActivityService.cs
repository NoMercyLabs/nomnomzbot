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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Viewer;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Services.Viewer;

/// <summary>
/// The signed-in viewer's own chat lines and runnable commands. Every query is keyed on the caller: messages
/// by the caller's platform id under the channel's provider, last use by the caller's user id.
/// </summary>
public sealed class ViewerSelfActivityService(
    IApplicationDbContext db,
    IRoleResolver roles,
    IBuiltinCommandService builtins
) : IViewerSelfActivityService
{
    public async Task<Result<PagedList<ViewerMessageDto>>> ListMessagesAsync(
        Guid channelId,
        Guid callerUserId,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        Channel? channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == channelId, ct);
        if (channel is null)
            return Errors.ChannelNotFound<PagedList<ViewerMessageDto>>(channelId.ToString());

        string? platformId = await ResolvePlatformIdAsync(callerUserId, channel.Provider, ct);
        if (platformId is null)
            return Result.Success(new PagedList<ViewerMessageDto>([], page, pageSize, 0));

        // A moderator deletion is a soft delete, so the global filter is lifted and the channel pinned by hand.
        IQueryable<NomNomzBot.Domain.Chat.Entities.ChatMessage> own = db
            .ChatMessages.IgnoreQueryFilters()
            .Where(m =>
                m.BroadcasterId == channelId
                && m.Provider == channel.Provider
                && m.UserId == platformId
            );
        int total = await own.CountAsync(ct);
        List<ViewerMessageDto> rows = await own.OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new ViewerMessageDto(m.Id, m.CreatedAt, m.Message, m.DeletedAt != null))
            .ToListAsync(ct);
        return Result.Success(new PagedList<ViewerMessageDto>(rows, page, pageSize, total));
    }

    public async Task<Result<IReadOnlyList<ViewerCommandDto>>> ListCommandsAsync(
        Guid channelId,
        Guid callerUserId,
        CancellationToken ct = default
    )
    {
        Result<int> level = await roles.ResolveEffectiveLevelAsync(callerUserId, channelId, ct);
        if (level.IsFailure)
            return level.WithValue<IReadOnlyList<ViewerCommandDto>>([]);

        List<ViewerCommandDto> custom = await db
            .Commands.Where(c =>
                c.BroadcasterId == channelId && c.IsEnabled && c.MinPermissionLevel <= level.Value
            )
            .Select(c => new ViewerCommandDto(
                c.Name,
                c.Description,
                ShownCooldown(c.CooldownSeconds, c.UserCooldownSeconds),
                null
            ))
            .ToListAsync(ct);
        List<ViewerCommandDto> builtIn = await ListRunnableBuiltinsAsync(
            channelId,
            level.Value,
            ct
        );
        Dictionary<string, DateTime> lastUsed = await LastUsedByNameAsync(
            channelId,
            callerUserId,
            ct
        );

        IReadOnlyList<ViewerCommandDto> ordered = custom
            .Concat(builtIn)
            .Select(c =>
                c with
                {
                    LastUsedAt = lastUsed.TryGetValue(Normalize(c.Name), out DateTime at)
                        ? at
                        : null,
                }
            )
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Result.Success(ordered);
    }

    private async Task<List<ViewerCommandDto>> ListRunnableBuiltinsAsync(
        Guid channelId,
        int callerLevel,
        CancellationToken ct
    )
    {
        Result<IReadOnlyList<BuiltinCommandDto>> listed = await builtins.ListAsync(
            channelId.ToString(),
            ct
        );
        if (listed.IsFailure)
            return [];

        return
        [
            .. listed
                .Value.Where(b => b.IsEnabled && MinLevel(b) <= callerLevel)
                .Select(b => new ViewerCommandDto(
                    b.Name,
                    null,
                    ShownCooldown(
                        b.CooldownSecondsOverride ?? b.DefaultCooldownSeconds,
                        b.UserCooldownSecondsOverride ?? b.DefaultUserCooldownSeconds
                    ),
                    null
                )),
        ];
    }

    /// <summary>The window a viewer waits: the channel-wide one when set, else their own.</summary>
    private static int? ShownCooldown(int globalSeconds, int userSeconds)
    {
        if (globalSeconds > 0)
            return globalSeconds;

        return userSeconds > 0 ? userSeconds : null;
    }

    private static int MinLevel(BuiltinCommandDto builtin) =>
        PermissionLevelNames.ToLevelValue(
            builtin.MinPermissionLevelOverride ?? builtin.DefaultMinPermissionLevel
        ) ?? int.MaxValue;

    private async Task<Dictionary<string, DateTime>> LastUsedByNameAsync(
        Guid channelId,
        Guid callerUserId,
        CancellationToken ct
    )
    {
        List<(string Name, DateTime At)> runs = (
            await db
                .CommandUsages.Where(u =>
                    u.BroadcasterId == channelId && u.ViewerUserId == callerUserId
                )
                .Select(u => new { u.CommandNameSnapshot, u.CreatedAt })
                .ToListAsync(ct)
        )
            .Select(u => (u.CommandNameSnapshot, u.CreatedAt))
            .ToList();

        return runs.GroupBy(r => Normalize(r.Name)).ToDictionary(g => g.Key, g => g.Max(r => r.At));
    }

    private static string Normalize(string name) => name.TrimStart('!').ToLowerInvariant();

    private async Task<string?> ResolvePlatformIdAsync(
        Guid userId,
        string provider,
        CancellationToken ct
    )
    {
        string? linked = await db
            .UserIdentities.Where(i => i.UserId == userId && i.Provider == provider)
            .Select(i => i.ProviderUserId)
            .FirstOrDefaultAsync(ct);
        if (linked is not null || provider != AuthEnums.Platform.Twitch)
            return linked;

        return await db
            .Users.Where(u => u.Id == userId)
            .Select(u => u.TwitchUserId)
            .FirstOrDefaultAsync(ct);
    }
}
