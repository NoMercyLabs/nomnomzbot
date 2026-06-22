// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.CustomCode.Entities;

/// <summary>
/// An immutable, validated version of a <see cref="CodeScript"/> (schema H.6, APPEND-ONLY). Validate-on-save
/// records the transpiled JS + hash + declared capabilities + the validation outcome; corrections are a NEW
/// version, never an edit (tamper-evident history). JSON columns are raw strings the service (de)serializes.
/// One row per <c>(script, version)</c>.
/// </summary>
public class CodeScriptVersion : ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CodeScriptId { get; set; }
    public Guid BroadcasterId { get; set; }
    public int Version { get; set; }
    public string SourceCode { get; set; } = null!;
    public string? CompiledJs { get; set; }
    public string? CompiledHash { get; set; }

    /// <summary><c>valid</c> | <c>rejected</c> | <c>pending</c>.</summary>
    public string ValidationStatus { get; set; } = "pending";

    /// <summary>Raw JSON of <c>IReadOnlyList&lt;ScriptValidationError&gt;</c> (null when valid).</summary>
    public string? ValidationErrorsJson { get; set; }

    /// <summary>Raw JSON of <c>IReadOnlyList&lt;string&gt;</c> capability keys.</summary>
    public string DeclaredCapabilitiesJson { get; set; } = "[]";
    public DateTime? PublishedAt { get; set; }
    public Guid? AuthorUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
