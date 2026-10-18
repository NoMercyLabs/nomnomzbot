// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
