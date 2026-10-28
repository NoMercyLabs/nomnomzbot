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

using NomNomzBot.Domain.CustomCode.ValueObjects;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.CustomCode.Events;

/// <summary>Raised after a version is persisted with its validate-on-save outcome (custom-code.md §2).</summary>
public sealed class CodeScriptValidatedEvent : DomainEventBase
{
    /// <summary>The id of the code script.</summary>
    public required Guid CodeScriptId { get; init; }

    /// <summary>The id of the script version that was checked.</summary>
    public required Guid CodeScriptVersionId { get; init; }

    /// <summary>The version number that was checked.</summary>
    public required int Version { get; init; }

    /// <summary>The result of the check, as text.</summary>
    public required string ValidationStatus { get; init; }

    /// <summary>The capabilities the script says it needs, as a list of names.</summary>
    public required IReadOnlyList<string> DeclaredCapabilities { get; init; }

    /// <summary>The problems the check found. The list is empty when the script is valid.</summary>
    public required IReadOnlyList<ScriptValidationError> Errors { get; init; }
}

/// <summary>Raised when CurrentVersionId is repointed — hot-swap; the old version stays immutable (§2).</summary>
public sealed class CodeScriptVersionPublishedEvent : DomainEventBase
{
    /// <summary>The id of the code script.</summary>
    public required Guid CodeScriptId { get; init; }

    /// <summary>The id of the version that was published.</summary>
    public required Guid CodeScriptVersionId { get; init; }

    /// <summary>The number of the published version.</summary>
    public required int Version { get; init; }

    /// <summary>The id of the version that was live before. Empty when this is the first published version.</summary>
    public Guid? PreviousVersionId { get; init; }

    /// <summary>The id of the user who published the version. Empty when the system published it.</summary>
    public Guid? PublishedByUserId { get; init; }
}
