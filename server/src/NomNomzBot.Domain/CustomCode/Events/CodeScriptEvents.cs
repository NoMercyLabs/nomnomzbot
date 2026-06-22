// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.CustomCode.ValueObjects;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.CustomCode.Events;

/// <summary>Raised after a version is persisted with its validate-on-save outcome (custom-code.md §2).</summary>
public sealed class CodeScriptValidatedEvent : DomainEventBase
{
    public required Guid CodeScriptId { get; init; }
    public required Guid CodeScriptVersionId { get; init; }
    public required int Version { get; init; }
    public required string ValidationStatus { get; init; }
    public required IReadOnlyList<string> DeclaredCapabilities { get; init; }
    public required IReadOnlyList<ScriptValidationError> Errors { get; init; }
}

/// <summary>Raised when CurrentVersionId is repointed — hot-swap; the old version stays immutable (§2).</summary>
public sealed class CodeScriptVersionPublishedEvent : DomainEventBase
{
    public required Guid CodeScriptId { get; init; }
    public required Guid CodeScriptVersionId { get; init; }
    public required int Version { get; init; }
    public Guid? PreviousVersionId { get; init; }
    public Guid? PublishedByUserId { get; init; }
}
