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
/// One installable platform-template kind (timer, event response, …). The admin side calls
/// <see cref="ValidatePayload"/> before a version is stored or published; the channel side calls
/// <see cref="InstallAsync"/> to copy a published version into the channel's own feature rows, through that
/// feature's own service so its validation and side effects run exactly as for a hand-made row.
/// </summary>
public interface IPlatformTemplateInstaller
{
    /// <summary>The <c>PlatformContentKinds</c> value this installer owns.</summary>
    string Kind { get; }

    /// <summary>The Gate-2 action key a caller needs in the target channel to install this kind.</summary>
    string WriteActionKey { get; }

    /// <summary>Checks the payload shape without touching any channel. Failure code: <c>VALIDATION_FAILED</c>.</summary>
    Result ValidatePayload(string payloadJson);

    Task<Result<InstalledPlatformTemplateDto>> InstallAsync(
        PlatformTemplateInstall install,
        CancellationToken ct = default
    );

    /// <summary>
    /// Every installed copy of the definition across ALL tenants, each with its live content hash, so a copy the
    /// channel edited since install can be told from one that still matches what it was copied from.
    /// </summary>
    Task<IReadOnlyList<PlatformContentCopy>> ListCopiesAsync(
        Guid definitionId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Rewrites one installed copy from a newer published payload through the SAME save path the channel's own
    /// page uses, then restamps its provenance. The copy keeps its identity and its channel-chosen bindings (a
    /// bound pipeline); a payload the copy cannot take (a pipeline template on a copy with no pipeline) fails
    /// <c>VALIDATION_FAILED</c> and leaves the copy as it was.
    /// </summary>
    Task<Result> UpdateCopyAsync(PlatformTemplateCopyUpdate update, CancellationToken ct = default);
}

/// <summary>
/// One tenant's installed copy of a definition, as the publish blast radius and the installs summary see it:
/// which version it was copied from, the hash recorded then, and the hash of what the row holds now.
/// </summary>
public sealed record PlatformContentCopy(
    Guid RowId,
    Guid BroadcasterId,
    int? SourceVersion,
    string? SourceHash,
    string LiveHash
);

public sealed record PlatformTemplateCopyUpdate(
    Guid RowId,
    Guid BroadcasterId,
    PlatformTemplateSource Source,
    string PayloadJson
);

/// <summary>The published version being installed — stamped onto the installed row as provenance.</summary>
public sealed record PlatformTemplateSource(Guid DefinitionId, int Version);

/// <summary>One install request, already authorized and resolved to a published version.</summary>
public sealed record PlatformTemplateInstall(
    Guid BroadcasterId,
    PlatformTemplateSource Source,
    string PayloadJson,
    Guid? PipelineId
);
