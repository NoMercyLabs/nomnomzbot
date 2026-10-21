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
/// Supplies the linked Discord guild's members for the <c>discord_user</c> resource-picker kind. The search
/// goes to Discord (a guild can hold far more members than one page), so a streamer finds a member by name and
/// never types an id. A value lookup reads the one saved member so it shows by name.
/// </summary>
internal sealed class DiscordUserOptionProvider
    : DiscordGuildOptionProviderBase,
        IPipelineOptionProvider
{
    private readonly IDiscordGuildDirectoryService _directory;

    public DiscordUserOptionProvider(
        IApplicationDbContext db,
        IDiscordGuildDirectoryService directory
    )
        : base(db)
    {
        _directory = directory;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.DiscordUser;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
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

        Guid connectionId = connectionResult.Value.Id;
        return request.IsValueLookup
            ? await LookupAsync(broadcasterId, connectionId, request.Value!, ct)
            : await SearchAsync(broadcasterId, connectionId, request, ct);
    }

    private async Task<Result<PipelineOptionListResult>> LookupAsync(
        Guid broadcasterId,
        Guid connectionId,
        string memberId,
        CancellationToken ct
    )
    {
        Result<DiscordGuildMemberDto> member = await _directory.GetGuildMemberAsync(
            broadcasterId,
            connectionId,
            memberId,
            ct
        );

        return Result.Success(
            member.IsSuccess
                ? PipelineOptionListResult.Of([ToOption(member.Value)], 1)
                : PipelineOptionListResult.Of([], 0)
        );
    }

    private async Task<Result<PipelineOptionListResult>> SearchAsync(
        Guid broadcasterId,
        Guid connectionId,
        PipelineOptionQuery request,
        CancellationToken ct
    )
    {
        Result<IReadOnlyList<DiscordGuildMemberDto>> members =
            await _directory.SearchGuildMembersAsync(
                broadcasterId,
                connectionId,
                request.Search,
                request.Pagination.PageSize,
                ct
            );
        if (members.IsFailure)
            return Result.Success(PipelineOptionListResult.Unavailable(Reason(members)));

        List<PipelineOption> page = [.. members.Value.Select(ToOption)];
        return Result.Success(PipelineOptionListResult.Of(page, page.Count));
    }

    // A streamer reads this line in the picker, so a missing bot token is said plainly, without config keys.
    private static string Reason(Result<IReadOnlyList<DiscordGuildMemberDto>> failure) =>
        failure.ErrorCode == "DISCORD_BOT_TOKEN_NOT_CONFIGURED"
            ? "The Discord bot is not set up on this bot server yet. The person who runs the bot server must add the Discord bot token."
            : failure.ErrorMessage ?? "Could not read the server's members from Discord.";

    private static PipelineOption ToOption(DiscordGuildMemberDto member) =>
        new(
            member.Id,
            member.DisplayName,
            $"@{member.Username}",
            member.AvatarUrl,
            PipelineOptionState.Selectable,
            null
        );
}
