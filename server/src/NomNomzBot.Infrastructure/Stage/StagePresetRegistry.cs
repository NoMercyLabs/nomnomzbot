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

namespace NomNomzBot.Infrastructure.Stage;

/// <summary>
/// Every <see cref="IStagePreset"/> class in the Infrastructure assembly, found by the same assembly scan the
/// other pluggable artifacts use, looked up by id. It is static because the action's <c>preset</c> options must be
/// readable without a built container (the SDK type emitter reads every action's fields that way); a new preset
/// class shows up in the options, the defaults and the lookup without another edit.
/// </summary>
public static class StagePresetRegistry
{
    private static readonly Lazy<IReadOnlyList<IStagePreset>> Discovered = new(Discover);

    /// <summary>Every preset, ordered by id.</summary>
    public static IReadOnlyList<IStagePreset> All => Discovered.Value;

    /// <summary>Every preset id, ordered; the options of the action's <c>preset</c> field.</summary>
    public static IReadOnlyList<string> Ids { get; } = [.. All.Select(p => p.Id)];

    /// <summary>The preset with this id, or null when the id is blank or names no preset.</summary>
    public static IStagePreset? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(p => string.Equals(p.Id, id.Trim(), StringComparison.Ordinal));

    private static IReadOnlyList<IStagePreset> Discover() =>
        [
            .. typeof(IStagePreset)
                .Assembly.GetTypes()
                .Where(t =>
                    t is { IsClass: true, IsAbstract: false }
                    && t.IsAssignableTo(typeof(IStagePreset))
                )
                .Select(t => (IStagePreset)Activator.CreateInstance(t)!)
                .OrderBy(p => p.Id, StringComparer.Ordinal),
        ];
}
