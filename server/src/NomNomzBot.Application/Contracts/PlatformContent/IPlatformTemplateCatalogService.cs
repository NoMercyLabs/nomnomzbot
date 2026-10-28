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
