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

namespace NomNomzBot.Application.Contracts.CustomCode;

/// <summary>Create a named script + its Version 1 (custom-code.md §4). No BroadcasterId (broker invariant).</summary>
public sealed record CreateCodeScriptRequest(string Name, string? Description, string SourceCode);

/// <summary>Append a new immutable version; <c>Publish</c> = save-and-swap on valid (custom-code.md §4).</summary>
public sealed record CreateCodeScriptVersionRequest(string SourceCode, bool Publish);

/// <summary>Toggle a script's enabled state (custom-code.md §5).</summary>
public sealed record SetCodeScriptEnabledRequest(bool IsEnabled);

public sealed record CodeScriptSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsEnabled,
    int? CurrentVersion,
    string CurrentValidationStatus,
    string? LastRuntimeError,
    DateTime? LastRanAt,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public sealed record CodeScriptDetailDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsEnabled,
    string Language,
    Guid? CurrentVersionId,
    CodeScriptVersionDto? CurrentVersion,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public sealed record CodeScriptVersionDto(
    Guid Id,
    Guid CodeScriptId,
    int Version,
    string SourceCode,
    string CompiledHash,
    string ValidationStatus,
    IReadOnlyList<ScriptValidationError> ValidationErrors,
    IReadOnlyList<string> DeclaredCapabilities,
    DateTime? PublishedAt,
    DateTime CreatedAt
);
