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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Commands.PipelineOptions;

/// <summary>
/// Supplies the asking channel's pipelines for the <c>pipeline</c> picker kind. The saved value is the pipeline id,
/// so a step keeps pointing at its pipeline when the pipeline is renamed. A saved id in the 26-character wire form
/// still finds its pipeline.
/// </summary>
internal sealed class PipelineIdOptionProvider : PipelineListOptionProvider
{
    public PipelineIdOptionProvider(IPipelineService pipelines)
        : base(pipelines) { }

    public override PipelineActionFieldKind Kind => PipelineActionFieldKind.Pipeline;

    protected override string ValueOf(PipelineListItemDto pipeline) => pipeline.Id.ToString();

    protected override PipelineOptionQuery Normalize(PipelineOptionQuery request) =>
        PipelineOptionPage.WithOwnedIdValue(request);
}
