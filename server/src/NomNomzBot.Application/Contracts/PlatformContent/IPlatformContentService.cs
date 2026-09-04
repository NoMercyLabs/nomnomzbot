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
/// Platform content authoring + propagation (platform-admin.md §2-§5) — draft/publish versioned platform
/// content and fan it out to installed tenant rows per one of the three publish modes (§2.1). This slice
/// implements <c>Kind = "command"</c> only (system commands, backed by <c>ChannelBuiltinCommand</c>); the
/// other kinds are carried by the <c>Kind</c> discriminator for follow-up slices. Every method re-asserts
/// the caller's Plane-C permission internally (the acting principal id is resolved by the controller via
/// <c>IIamCallerPrincipalResolverService</c>, matching <c>PlatformIamController</c>'s convention) and audits
/// per §5. SaaS-only.
/// </summary>
public interface IPlatformContentService
{
    Task<Result<PagedList<PlatformContentDefinitionDto>>> ListDefinitionsAsync(
        Guid actingPrincipalId,
        string? kind,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task<Result<PlatformContentDefinitionDetailDto>> GetDefinitionAsync(
        Guid actingPrincipalId,
        Guid definitionId,
        CancellationToken ct = default
    );

    Task<Result<PlatformContentDefinitionDto>> CreateDefinitionAsync(
        Guid actingPrincipalId,
        CreateContentDefinitionRequest request,
        CancellationToken ct = default
    );

    Task<Result<PlatformContentVersionDto>> DraftVersionAsync(
        Guid actingPrincipalId,
        Guid definitionId,
        DraftContentVersionRequest request,
        CancellationToken ct = default
    );

    Task<Result<PlatformContentVersionDto>> GetVersionAsync(
        Guid actingPrincipalId,
        Guid definitionId,
        Guid versionId,
        CancellationToken ct = default
    );

    Task<Result<PublishPreviewDto>> PreviewPublishAsync(
        Guid actingPrincipalId,
        Guid definitionId,
        Guid versionId,
        string mode,
        CancellationToken ct = default
    );

    Task<Result<PlatformContentPublishJobDto>> PublishAsync(
        Guid actingPrincipalId,
        Guid definitionId,
        Guid versionId,
        PublishContentRequest request,
        CancellationToken ct = default
    );

    Task<Result<PlatformContentPublishJobDto>> GetPublishJobAsync(
        Guid actingPrincipalId,
        Guid publishJobId,
        CancellationToken ct = default
    );

    Task<Result> RetireDefinitionAsync(
        Guid actingPrincipalId,
        Guid definitionId,
        CancellationToken ct = default
    );
}
