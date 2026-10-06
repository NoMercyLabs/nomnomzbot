// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Pipeline;

/// <summary>
/// The variable names the pipeline engine itself writes for later steps. The engine writes them and the
/// save-time template guard accepts them, both from this one list.
/// </summary>
public static class PipelineEngineVariables
{
    public const string LastSuccess = "last.success";
    public const string LastOutput = "last.output";
    public const string LastError = "last.error";
    public const string LoopIndex = "loop.index";
    public const string LoopItem = "loop.item";
    public const string LoopPreviousItem = "loop.previous_item";
    public const string LoopCount = "loop.count";
    public const string CallResult = "call.result";

    public static readonly IReadOnlyList<string> All =
    [
        LastSuccess,
        LastOutput,
        LastError,
        LoopIndex,
        LoopItem,
        LoopPreviousItem,
        LoopCount,
        CallResult,
    ];
}
