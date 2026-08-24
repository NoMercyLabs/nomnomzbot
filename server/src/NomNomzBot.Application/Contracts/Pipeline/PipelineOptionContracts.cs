// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.Pipeline;

/// <summary>Whether one option can currently be picked (S-RICH-PICKERS).</summary>
public enum PipelineOptionState
{
    Selectable,
    Unavailable,
}

/// <summary>
/// One item a resource-picker field can offer, in the ONE generic shape every
/// <see cref="PipelineActionFieldKind"/> resource-picker kind returns — never a bespoke DTO per kind.
/// </summary>
/// <param name="Value">The stable id actually stored in the action's parameter (what gets persisted).</param>
/// <param name="Label">Human-readable name shown in the picker.</param>
/// <param name="SecondaryText">
/// What genuinely identifies this item in context beyond the label (cost, locale, duration, login vs display
/// name, …) — kind-specific, populated from the real source, never fabricated.
/// </param>
/// <param name="ImageUrl">Optional preview/avatar/icon url; null when the source has none.</param>
/// <param name="State">Whether the item is currently pickable.</param>
/// <param name="Reason">Populated when <see cref="State"/> is <see cref="PipelineOptionState.Unavailable"/>.</param>
public sealed record PipelineOption(
    string Value,
    string Label,
    string? SecondaryText,
    string? ImageUrl,
    PipelineOptionState State,
    string? Reason = null
);

/// <summary>
/// The result of resolving a picker kind's option list. <see cref="SourceAvailable"/> distinguishes a
/// genuinely empty list (the tenant has zero rewards) from a source that could not be read at all (Discord not
/// linked, the guild call failed) — never collapsed into the same empty response (truthful-data rule).
/// </summary>
/// <param name="SourceAvailable">False when the underlying source is not connected/reachable for this tenant.</param>
/// <param name="UnavailableReason">Populated when <see cref="SourceAvailable"/> is false.</param>
/// <param name="Items">The page of options; empty (with <see cref="SourceAvailable"/> true) is a real empty list.</param>
/// <param name="TotalCount">Total items matching the search, for pagination — 0 when unavailable.</param>
public sealed record PipelineOptionListResult(
    bool SourceAvailable,
    string? UnavailableReason,
    IReadOnlyList<PipelineOption> Items,
    int TotalCount
)
{
    public static PipelineOptionListResult Unavailable(string reason) => new(false, reason, [], 0);

    public static PipelineOptionListResult Of(
        IReadOnlyList<PipelineOption> items,
        int totalCount
    ) => new(true, null, items, totalCount);
}

/// <summary>
/// Resolves the option list for ONE <see cref="PipelineActionFieldKind"/> resource-picker kind — the supply
/// side of the S045 field schema (which declares WHAT to pick; this supplies the recognisable list to pick
/// FROM). One implementation per picker kind, registered generically via the assembly scan
/// (<c>AddImplementationsOf&lt;IPipelineOptionProvider&gt;</c>) and resolved by <see cref="Kind"/> through
/// <see cref="IPipelineOptionRegistry"/> — never a growing <c>switch</c>.
/// </summary>
public interface IPipelineOptionProvider
{
    /// <summary>The resource-picker kind this provider supplies options for.</summary>
    PipelineActionFieldKind Kind { get; }

    /// <summary>
    /// Resolves the tenant-scoped option page. <paramref name="search"/> filters by label/secondary text when
    /// the source supports it; null/empty returns the unfiltered page.
    /// </summary>
    Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default
    );
}

/// <summary>
/// Resolves the registered <see cref="IPipelineOptionProvider"/> for a given
/// <see cref="PipelineActionFieldKind"/> and dispatches to it. The single entry point the picker-options
/// endpoint calls — never a hand-written per-kind <c>switch</c>.
/// </summary>
public interface IPipelineOptionRegistry
{
    Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        PipelineActionFieldKind kind,
        Guid broadcasterId,
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default
    );
}
