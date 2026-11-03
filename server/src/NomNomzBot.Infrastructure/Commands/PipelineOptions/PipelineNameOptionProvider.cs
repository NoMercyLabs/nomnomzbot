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

namespace NomNomzBot.Infrastructure.Commands.PipelineOptions;

/// <summary>
/// Supplies the asking channel's pipelines for the <c>pipeline_name</c> picker kind. The saved value is the pipeline
/// name, because the action that uses it schedules a pipeline by name.
/// </summary>
internal sealed class PipelineNameOptionProvider : PipelineListOptionProvider
{
    public PipelineNameOptionProvider(IPipelineService pipelines)
        : base(pipelines) { }

    public override PipelineActionFieldKind Kind => PipelineActionFieldKind.PipelineName;

    protected override string ValueOf(PipelineListItemDto pipeline) => pipeline.Name;
}
