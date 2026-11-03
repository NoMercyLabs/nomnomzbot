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

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Identity.Pipeline;

/// <summary>
/// Supplies the pronoun sets of the pronoun provider's catalogue for the <c>pronoun</c> picker kind, plus the
/// <c>clear</c> choice that removes a set pronoun. The saved value is the pronoun name as the provider writes it. A
/// catalogue the provider cannot return is unavailable, with the reason.
/// </summary>
internal sealed class PronounOptionProvider : IPipelineOptionProvider
{
    private const string ClearValue = "clear";

    private readonly IPronounProvider _pronouns;

    public PronounOptionProvider(IPronounProvider pronouns)
    {
        _pronouns = pronouns;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.Pronoun;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        IReadOnlyDictionary<string, PronounCatalogEntry>? catalog = await _pronouns.GetCatalogAsync(
            ct
        );
        if (catalog is null)
            return PipelineOptionPage.Unavailable("The pronoun catalogue could not be loaded.");

        IEnumerable<PipelineOption> sets = catalog.Values.Select(e =>
            PipelineOptionPage.Option(e.Name, e.Name)
        );
        return PipelineOptionPage.Of(
            [PipelineOptionPage.Option(ClearValue, ClearValue), .. sets],
            request
        );
    }
}
