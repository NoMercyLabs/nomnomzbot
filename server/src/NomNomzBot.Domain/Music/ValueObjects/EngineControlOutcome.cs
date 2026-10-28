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

namespace NomNomzBot.Domain.Music.ValueObjects;

/// <summary>The outcome of an engine control that not every engine can perform.</summary>
public enum EngineControlOutcome
{
    /// <summary>The engine did it.</summary>
    Done,

    /// <summary>The engine has no such control; the dashboard gates on the engine's capabilities.</summary>
    NotSupported,

    /// <summary>The engine supports it but a precondition is missing (see <see cref="EngineBlockReason"/>).</summary>
    Blocked,
}
