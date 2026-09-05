// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.PlatformContent;

/// <summary>A platform content definition summary row (platform-admin.md §3.1) — the definitions list.</summary>
public sealed record PlatformContentDefinitionDto(
    Guid Id,
    string Kind,
    string Key,
    string DisplayName,
    string? Description,
    Guid? CurrentVersionId,
    int? CurrentVersion,
    Guid? LatestDraftVersionId,
    DateTime CreatedAt,
    DateTime? RetiredAt
);

/// <summary>A definition plus its full version history (§4 <c>GET /definitions/{id}</c>).</summary>
public sealed record PlatformContentDefinitionDetailDto(
    PlatformContentDefinitionDto Definition,
    IReadOnlyList<PlatformContentVersionDto> Versions
);

/// <summary>One immutable content version (§3.2).</summary>
public sealed record PlatformContentVersionDto(
    Guid Id,
    Guid DefinitionId,
    int Version,
    string ContentHash,
    string PayloadJson,
    IReadOnlyList<string> RenderGalleryRefs,
    string? PublishNote,
    DateTime DraftedAt,
    Guid DraftedByPrincipalId,
    DateTime? PublishedAt,
    Guid? PublishedByPrincipalId
);

public sealed record CreateContentDefinitionRequest(
    string Kind,
    string Key,
    string DisplayName,
    string? Description,
    string PayloadJson
);

public sealed record DraftContentVersionRequest(
    string PayloadJson,
    IReadOnlyList<string>? RenderGalleryRefs
);

public sealed record PublishPreviewRequest(string Mode);

/// <summary>The blast-radius preview (§2.1) — must be rendered before a confirm button enables.</summary>
public sealed record PublishPreviewDto(
    int AffectedCount,
    int SkippedCount,
    IReadOnlyList<string> SampleTenantNames
);

public sealed record PublishContentRequest(
    string Mode,
    string? PublishNote,
    int ConfirmedPreviewAffectedCount
);

/// <summary>One publish attempt (§3.4).</summary>
public sealed record PlatformContentPublishJobDto(
    Guid Id,
    Guid DefinitionId,
    int? FromVersion,
    int ToVersion,
    string Mode,
    Guid RequestedByPrincipalId,
    DateTime RequestedAt,
    int PreviewAffectedCount,
    int PreviewSkippedCount,
    int? ConfirmedAffectedCount,
    string Status,
    DateTime? CompletedAt,
    string? FailureReason,
    IReadOnlyList<Guid> RebuildFailedWidgetIds,
    IReadOnlyList<Guid> ValidationFailedPipelineIds
);
