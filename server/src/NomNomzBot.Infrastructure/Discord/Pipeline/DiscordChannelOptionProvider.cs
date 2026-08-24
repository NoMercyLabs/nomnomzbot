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
/// Supplies the linked Discord guild's live channel list for the <c>discord_channel</c> resource-picker kind
/// (S-RICH-PICKERS). <see cref="PipelineOption.SecondaryText"/> is the channel type (text/voice/category/…)
/// plus its parent category, when set — real Discord data, never a fabricated label.
/// </summary>
internal sealed class DiscordChannelOptionProvider
    : DiscordGuildOptionProviderBase,
        IPipelineOptionProvider
{
    private readonly IDiscordGuildDirectoryService _directory;

    public DiscordChannelOptionProvider(
        IApplicationDbContext db,
        IDiscordGuildDirectoryService directory
    )
        : base(db)
    {
        _directory = directory;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.DiscordChannel;

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

        Result<IReadOnlyList<DiscordGuildChannelDto>> channels =
            await _directory.GetGuildChannelsAsync(broadcasterId, connectionResult.Value.Id, ct);
        if (channels.IsFailure)
            return Result.Success(
                PipelineOptionListResult.Unavailable(
                    channels.ErrorMessage ?? "Could not read the guild's channels from Discord."
                )
            );

        Dictionary<string, string?> namesById = channels.Value.ToDictionary(c => c.Id, c => c.Name);

        IEnumerable<DiscordGuildChannelDto> filtered = channels.Value;
        if (!string.IsNullOrWhiteSpace(search))
            filtered = filtered.Where(c =>
                c.Name is not null && c.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            );

        List<DiscordGuildChannelDto> ordered = [.. filtered.OrderBy(c => c.Position)];
        int total = ordered.Count;
        List<PipelineOption> page =
        [
            .. ordered
                .Skip((pagination.Page - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .Select(c => ToOption(c, namesById)),
        ];

        return Result.Success(PipelineOptionListResult.Of(page, total));
    }

    private static PipelineOption ToOption(
        DiscordGuildChannelDto channel,
        IReadOnlyDictionary<string, string?> namesById
    )
    {
        string typeName = ChannelTypeName(channel.Type);
        string? parentName =
            channel.ParentId is not null
            && namesById.TryGetValue(channel.ParentId, out string? name)
                ? name
                : null;
        string secondaryText = parentName is null ? typeName : $"{typeName} · {parentName}";

        return new PipelineOption(
            channel.Id,
            channel.Name ?? channel.Id,
            secondaryText,
            ImageUrl: null,
            PipelineOptionState.Selectable
        );
    }

    // Discord channel type ids (discord.md §3.5) — 0 text, 2 voice, 4 category, 5 announcement, 13 stage, 15 forum.
    private static string ChannelTypeName(int type) =>
        type switch
        {
            0 => "Text",
            2 => "Voice",
            4 => "Category",
            5 => "Announcement",
            13 => "Stage",
            15 => "Forum",
            _ => "Channel",
        };
}
