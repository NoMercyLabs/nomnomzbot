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

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// What a scan of the tenant's <c>PipelineStep.ConfigJson</c> blobs found. <see cref="MatchCount"/> is the
/// number of steps whose stored config literally names the resource; <see cref="PipelineNames"/> names the
/// distinct pipelines those steps live in, so the user recognises WHAT breaks.
/// </summary>
/// <param name="IsMinimum">
/// True when the tenant also holds steps whose reference cannot be read from stored config — a scanned field
/// holding a <c>{{template}}</c> placeholder, a <c>run_code</c> step that resolves resources through the SDK
/// at run time, or a config blob that is not readable JSON. In that case the match count is a verified FLOOR,
/// not a total, and the confirm surface must say so.
/// </param>
public sealed record PipelineStepReferenceScan(
    int MatchCount,
    IReadOnlyList<string> PipelineNames,
    bool IsMinimum
);

/// <summary>
/// Counts the pipeline steps that reference a resource which has NO foreign key — sound clips and widgets are
/// named inside the opaque <c>PipelineStep.ConfigJson</c> blob, so the database cannot answer "what breaks if
/// I delete this?" with a join. This scanner answers it by reading the stored config, and is deliberately
/// explicit about the part it cannot see (see <see cref="PipelineStepReferenceScan.IsMinimum"/>) rather than
/// reporting a total it has not earned.
/// </summary>
public interface IPipelineStepReferenceScanner
{
    /// <summary>
    /// Scan every pipeline step in <paramref name="broadcasterId"/> for a top-level config field named in
    /// <paramref name="fieldNames"/> whose string value equals any of <paramref name="tokens"/>
    /// (case-insensitive). A resource referenced by id OR by name passes both as tokens.
    /// </summary>
    Task<Result<PipelineStepReferenceScan>> ScanAsync(
        Guid broadcasterId,
        IReadOnlyList<string> fieldNames,
        IReadOnlyList<string> tokens,
        CancellationToken ct = default
    );
}
