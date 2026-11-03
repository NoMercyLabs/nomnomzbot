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

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Commands.PipelineOptions;

/// <summary>
/// Supplies the asking channel's commands for the <c>command_name</c> picker kind. The saved value is the command
/// name; the secondary text is the command's description, or its tier when it has none. It reads every page of the
/// service, and a list that cannot be read is unavailable with the service's reason.
/// </summary>
internal sealed class CommandNameOptionProvider : IPipelineOptionProvider
{
    private readonly ICommandService _commands;

    public CommandNameOptionProvider(ICommandService commands)
    {
        _commands = commands;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.CommandName;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        List<CommandListItem> items = [];
        int page = 1;
        bool more = true;
        while (more)
        {
            Result<PagedList<CommandListItem>> read = await _commands.ListAsync(
                broadcasterId.ToString(),
                new PaginationParams(page, PaginationParams.MaxPageSize),
                ct
            );
            if (read.IsFailure)
                return PipelineOptionPage.Unavailable(read.ErrorMessage!);

            items.AddRange(read.Value.Items);
            more = read.Value.HasNextPage;
            page++;
        }

        return PipelineOptionPage.Of(items.Select(ToOption), request);
    }

    private static PipelineOption ToOption(CommandListItem command) =>
        PipelineOptionPage.Option(command.Name, command.Name, command.Description ?? command.Tier);
}
