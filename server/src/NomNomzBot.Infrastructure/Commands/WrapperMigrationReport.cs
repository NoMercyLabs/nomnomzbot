// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>One surface row moved from a wrapper pipeline to the command it wrapped. The old pipeline id is kept for the report.</summary>
public sealed record ReboundBinding(string Surface, Guid RowId, Guid OldPipelineId, Guid CommandId);

/// <summary>A bound pipeline that stayed a pipeline binding, with the single reason it is not a safe wrapper.</summary>
public sealed record KeptWrapper(Guid PipelineId, string Reason);

/// <summary>A Stream Deck key wrapper (a pipeline named by its one action type) switched off so the key runs the action target directly. The pipeline row stays.</summary>
public sealed record RetiredStreamDeckWrapper(Guid PipelineId, string ActionType);

/// <summary>What one wrapper-migration pass did: every rebound row, every kept pipeline with its reason, and every Stream Deck key wrapper retired.</summary>
public sealed record WrapperMigrationReport(
    IReadOnlyList<ReboundBinding> Rebound,
    IReadOnlyList<KeptWrapper> Kept,
    IReadOnlyList<RetiredStreamDeckWrapper> StreamDeck
);
