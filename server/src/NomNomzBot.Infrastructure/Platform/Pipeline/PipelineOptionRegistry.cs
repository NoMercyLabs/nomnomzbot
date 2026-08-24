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
using NomNomzBot.Application.Contracts.Pipeline;

namespace NomNomzBot.Infrastructure.Platform.Pipeline;

/// <summary>
/// Keys the assembly-scanned <see cref="IPipelineOptionProvider"/> set by <see cref="IPipelineOptionProvider.Kind"/>
/// (S-RICH-PICKERS). Throws at construction if two providers claim the same kind — the same ambiguity guard
/// <c>AddServicesByConvention</c> uses, so a duplicate registration fails loudly at startup rather than
/// silently picking one.
/// </summary>
public sealed class PipelineOptionRegistry : IPipelineOptionRegistry
{
    private readonly IReadOnlyDictionary<
        PipelineActionFieldKind,
        IPipelineOptionProvider
    > _providers;

    public PipelineOptionRegistry(IEnumerable<IPipelineOptionProvider> providers)
    {
        Dictionary<PipelineActionFieldKind, IPipelineOptionProvider> byKind = [];
        foreach (IPipelineOptionProvider provider in providers)
        {
            if (byKind.TryGetValue(provider.Kind, out IPipelineOptionProvider? existing))
                throw new InvalidOperationException(
                    $"Ambiguous pipeline option provider: kind '{provider.Kind}' is claimed by both "
                        + $"'{existing.GetType().Name}' and '{provider.GetType().Name}'."
                );
            byKind[provider.Kind] = provider;
        }
        _providers = byKind;
    }

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        PipelineActionFieldKind kind,
        Guid broadcasterId,
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        if (!_providers.TryGetValue(kind, out IPipelineOptionProvider? provider))
            return Result.Failure<PipelineOptionListResult>(
                $"No option provider is registered for picker kind '{kind.ToWireName()}'.",
                "UNSUPPORTED_KIND"
            );

        return await provider.GetOptionsAsync(broadcasterId, search, pagination, ct);
    }
}
