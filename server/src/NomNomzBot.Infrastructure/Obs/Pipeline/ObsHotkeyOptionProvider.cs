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
using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Infrastructure.Obs.Pipeline;

/// <summary>
/// Supplies the hotkey names the channel's connected OBS knows for the <c>obs_hotkey</c> picker kind. OBS reports a
/// bare name per hotkey, so there is no secondary text. With OBS not connected the list is unavailable, with the
/// reason OBS gave, rather than empty.
/// </summary>
internal sealed class ObsHotkeyOptionProvider : IPipelineOptionProvider
{
    private readonly IObsControlService _obs;

    public ObsHotkeyOptionProvider(IObsControlService obs)
    {
        _obs = obs;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.ObsHotkey;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        Result<IReadOnlyList<string>> hotkeys = await _obs.GetHotkeyListAsync(broadcasterId, ct);
        if (hotkeys.IsFailure)
            return ObsOptionPage.Unavailable(hotkeys.ErrorMessage!);

        return ObsOptionPage.Of(
            hotkeys.Value.Distinct().Select(h => ObsOptionPage.Named(h, secondaryText: null)),
            request
        );
    }
}
