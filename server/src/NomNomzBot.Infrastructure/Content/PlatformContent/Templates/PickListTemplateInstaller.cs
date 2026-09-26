// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.PickLists.Dtos;
using NomNomzBot.Application.PickLists.Services;
using NomNomzBot.Domain.PickLists.Entities;
using NomNomzBot.Domain.PlatformContent.Entities;

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Templates;

/// <summary>
/// Installs a <c>pick_list</c> template: adds the list through <see cref="IPickListService.CreateAsync"/> (the
/// same save path as the Pick Lists page), then stamps provenance. The name is the <c>{list.pick.name}</c> key,
/// so it is never renamed: a channel that already has a list with that name gets <c>ALREADY_EXISTS</c> instead
/// of a silently different key.
/// </summary>
public sealed partial class PickListTemplateInstaller(
    IApplicationDbContext db,
    IPickListService pickLists
) : IPlatformTemplateInstaller
{
    // The limits PickListService enforces on save.
    private const int MaxNameLength = 100;
    private const int MaxDescriptionLength = 500;
    private const int MaxItems = 500;
    private const int MaxItemLength = 500;

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex NamePattern();

    public string Kind => PlatformContentKinds.PickList;

    public string WriteActionKey => "picklists:write";

    public Result ValidatePayload(string payloadJson)
    {
        Result<PickListTemplatePayload> parsed =
            PlatformTemplateJson.Parse<PickListTemplatePayload>(payloadJson);
        return parsed.IsFailure ? parsed : Validate(parsed.Value);
    }

    public async Task<Result<InstalledPlatformTemplateDto>> InstallAsync(
        PlatformTemplateInstall install,
        CancellationToken ct = default
    )
    {
        Result<PickListTemplatePayload> parsed =
            PlatformTemplateJson.Parse<PickListTemplatePayload>(install.PayloadJson);
        if (parsed.IsFailure)
            return parsed.WithValue<InstalledPlatformTemplateDto>(null!);
        PickListTemplatePayload payload = parsed.Value;

        Result valid = Validate(payload);
        if (valid.IsFailure)
            return valid.WithValue<InstalledPlatformTemplateDto>(null!);

        Result<PickListDto> created = await pickLists.CreateAsync(
            install.BroadcasterId,
            new(payload.Name.Trim(), payload.Description, [.. payload.Items]),
            ct
        );
        if (created.IsFailure)
            return created.WithValue<InstalledPlatformTemplateDto>(null!);

        PickList row = await db.PickLists.FirstAsync(
            p => p.Id == created.Value.Id && p.BroadcasterId == install.BroadcasterId,
            ct
        );
        row.Stamp(
            install.Source,
            PickListTemplatePayload.FromEntity(row).ComputeHash(),
            DateTime.UtcNow
        );
        await db.SaveChangesAsync(ct);

        return Result.Success(new InstalledPlatformTemplateDto(Kind, row.Id, row.Name));
    }

    private static Result Validate(PickListTemplatePayload payload)
    {
        string name = payload.Name.Trim();
        if (name.Length == 0)
            return Failure("A pick list template needs a name.");
        if (name.Length > MaxNameLength)
            return Failure($"The list name is longer than {MaxNameLength} characters.");
        if (!NamePattern().IsMatch(name))
            return Failure(
                "A list name may contain only letters, numbers, underscores and hyphens."
            );
        if (payload.Description is { Length: > MaxDescriptionLength })
            return Failure($"The description is longer than {MaxDescriptionLength} characters.");

        List<string> items = [.. payload.Items.Where(i => !string.IsNullOrWhiteSpace(i))];
        if (items.Count == 0)
            return Failure("A pick list template needs at least one entry.");
        if (items.Count > MaxItems)
            return Failure($"A pick list holds at most {MaxItems} entries.");
        if (items.Any(i => i.Trim().Length > MaxItemLength))
            return Failure($"An entry is longer than {MaxItemLength} characters.");
        return Result.Success();
    }

    private static Result Failure(string message) => Result.Failure(message, "VALIDATION_FAILED");
}
