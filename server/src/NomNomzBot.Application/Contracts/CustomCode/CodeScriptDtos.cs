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

namespace NomNomzBot.Application.Contracts.CustomCode;

/// <summary>Create a named script + its Version 1 (custom-code.md §4). No BroadcasterId (broker invariant).</summary>
public sealed record CreateCodeScriptRequest(string Name, string? Description, string SourceCode);

/// <summary>Append a new immutable version; <c>Publish</c> = save-and-swap on valid (custom-code.md §4).</summary>
public sealed record CreateCodeScriptVersionRequest(string SourceCode, bool Publish);

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
