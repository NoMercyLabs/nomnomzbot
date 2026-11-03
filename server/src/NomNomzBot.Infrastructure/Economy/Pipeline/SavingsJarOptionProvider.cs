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
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Economy.Pipeline;

/// <summary>
/// Supplies the savings jars the asking channel can contribute to for the <c>savings_jar</c> picker kind. The saved
/// value is the jar id; the secondary text is the balance, with the goal when the jar has one. A saved id in the
/// 26-character wire form still finds its jar. A jar list that cannot be read is unavailable, with the reason.
/// </summary>
internal sealed class SavingsJarOptionProvider : IPipelineOptionProvider
{
    private readonly ISavingsJarService _jars;

    public SavingsJarOptionProvider(ISavingsJarService jars)
    {
        _jars = jars;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.SavingsJar;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        Result<IReadOnlyList<SavingsJarDto>> jars = await _jars.ListJarsForChannelAsync(
            broadcasterId,
            ct
        );
        if (jars.IsFailure)
            return PipelineOptionPage.Unavailable(jars.ErrorMessage!);

        return PipelineOptionPage.Of(
            jars.Value.Select(ToOption),
            PipelineOptionPage.WithOwnedIdValue(request)
        );
    }

    private static PipelineOption ToOption(SavingsJarDto jar) =>
        PipelineOptionPage.Option(
            jar.Id.ToString(),
            jar.Name,
            jar.GoalAmount is null ? $"{jar.Balance}" : $"{jar.Balance} of {jar.GoalAmount}"
        );
}
