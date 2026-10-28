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
