// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Domain.Discord.Entities;

namespace NomNomzBot.Infrastructure.Discord.Pipeline;

/// <summary>
/// Supplies the linked Discord guild's live role list for the <c>discord_role</c> resource-picker kind
/// (S-RICH-PICKERS). <see cref="PipelineOption.SecondaryText"/> carries the role's colour (hex) and whether it
/// is mentionable — the two facts that actually distinguish one role from another in the picker.
/// </summary>
internal sealed class DiscordRoleOptionProvider
    : DiscordGuildOptionProviderBase,
        IPipelineOptionProvider
{
    private readonly IDiscordGuildDirectoryService _directory;

    public DiscordRoleOptionProvider(
        IApplicationDbContext db,
        IDiscordGuildDirectoryService directory
    )
        : base(db)
    {
        _directory = directory;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.DiscordRole;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        Result<DiscordGuildConnection> connectionResult = await ResolveActiveConnectionAsync(
            broadcasterId,
            ct
        );
        if (connectionResult.IsFailure)
            return Result.Success(
                PipelineOptionListResult.Unavailable(connectionResult.ErrorMessage!)
            );

        Result<IReadOnlyList<DiscordGuildRoleDto>> roles = await _directory.GetGuildRolesAsync(
            broadcasterId,
            connectionResult.Value.Id,
            ct
        );
        if (roles.IsFailure)
            return Result.Success(
                PipelineOptionListResult.Unavailable(
                    roles.ErrorMessage ?? "Could not read the guild's roles from Discord."
                )
            );

        IEnumerable<DiscordGuildRoleDto> filtered = roles.Value;
        if (!string.IsNullOrWhiteSpace(search))
            filtered = filtered.Where(r =>
                r.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            );

        List<DiscordGuildRoleDto> ordered = [.. filtered.OrderByDescending(r => r.Position)];
        int total = ordered.Count;
        List<PipelineOption> page =
        [
            .. ordered
                .Skip((pagination.Page - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .Select(ToOption),
        ];

        return Result.Success(PipelineOptionListResult.Of(page, total));
    }

    private static PipelineOption ToOption(DiscordGuildRoleDto role)
    {
        string hex = $"#{role.Color:X6}";
        string secondaryText = $"{hex} · {(role.Mentionable ? "mentionable" : "not mentionable")}";

        return new PipelineOption(
            role.Id,
            role.Name,
            secondaryText,
            ImageUrl: null,
            role.Managed ? PipelineOptionState.Unavailable : PipelineOptionState.Selectable,
            role.Managed ? "Managed by an integration — cannot be self-assigned." : null
        );
    }
}
