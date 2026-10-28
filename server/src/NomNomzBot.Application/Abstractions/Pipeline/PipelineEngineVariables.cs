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
