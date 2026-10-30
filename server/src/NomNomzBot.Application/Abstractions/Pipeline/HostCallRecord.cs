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
/// One host call a step made (for example <c>tts.speak</c>): how long it took, whether it answered, and why
/// not when it did not. Holds no arguments and no answer, so it carries no viewer content.
/// </summary>
public sealed record HostCallRecord(string Name, int DurationMs, bool Ok, string? Error);
