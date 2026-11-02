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
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Infrastructure.Obs.Pipeline;

/// <summary>
/// Supplies every input of the channel's connected OBS for the <c>obs_input</c> picker kind (media, browser, image,
/// audio). Unlike <see cref="ObsAudioInputOptionProvider"/> it keeps inputs that have no mute state. The input kind
/// OBS reports is the secondary text. With OBS not connected the list is unavailable, with the reason OBS gave.
/// </summary>
internal sealed class ObsInputOptionProvider : IPipelineOptionProvider
{
    private readonly IObsControlService _obs;

    public ObsInputOptionProvider(IObsControlService obs)
    {
        _obs = obs;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.ObsInput;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        Result<IReadOnlyList<ObsInputDto>> inputs = await _obs.GetInputsAsync(broadcasterId, ct);
        if (inputs.IsFailure)
            return ObsOptionPage.Unavailable(inputs.ErrorMessage!);

        return ObsOptionPage.Of(
            inputs.Value.Select(i => ObsOptionPage.Named(i.Name, i.Kind)),
            request
        );
    }
}
