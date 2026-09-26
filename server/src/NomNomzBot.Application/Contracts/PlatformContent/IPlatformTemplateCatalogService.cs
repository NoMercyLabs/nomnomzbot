// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.PlatformContent;

/// <summary>
/// The channel side of platform templates: browse the published catalogue for one kind and install a copy
/// into this channel. Install checks the caller's Gate-2 write key for that kind in the target channel.
/// </summary>
public interface IPlatformTemplateCatalogService
{
    Task<Result<PagedList<PlatformTemplateDto>>> ListAsync(
        string kind,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<Result<InstalledPlatformTemplateDto>> InstallAsync(
        Guid callerUserId,
        Guid broadcasterId,
        Guid definitionId,
        InstallPlatformTemplateRequest request,
        CancellationToken ct = default
    );
}
