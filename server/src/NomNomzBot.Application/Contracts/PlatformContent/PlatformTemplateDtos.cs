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

/// <summary>One published platform template a channel can install (its current published version).</summary>
public sealed record PlatformTemplateDto(
    Guid DefinitionId,
    string Kind,
    string Key,
    string DisplayName,
    string? Description,
    int Version,
    string PayloadJson
);

/// <summary>
/// Install options. <see cref="PipelineId"/> binds a pipeline for kinds that run one — it must belong to the
/// installing channel (a template never carries a pipeline id of its own).
/// </summary>
public sealed record InstallPlatformTemplateRequest(Guid? PipelineId);

/// <summary>The channel row an install created or replaced.</summary>
public sealed record InstalledPlatformTemplateDto(string Kind, Guid EntityId, string Name);
