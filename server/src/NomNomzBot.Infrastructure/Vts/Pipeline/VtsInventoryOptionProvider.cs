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
using NomNomzBot.Application.Vts.Dtos;
using NomNomzBot.Application.Vts.Services;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Vts.Pipeline;

/// <summary>
/// The shared read of the channel's VTube Studio inventory for the three VTS picker kinds. Each kind picks its own
/// slice of the inventory. With VTube Studio not connected the list is unavailable, with the reason VTS gave, rather
/// than an empty list.
/// </summary>
internal abstract class VtsInventoryOptionProvider : IPipelineOptionProvider
{
    private readonly IVtsControlService _vts;

    protected VtsInventoryOptionProvider(IVtsControlService vts)
    {
        _vts = vts;
    }

    public abstract PipelineActionFieldKind Kind { get; }

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        Result<VtsModelInventory> inventory = await _vts.GetInventoryAsync(broadcasterId, ct);
        if (inventory.IsFailure)
            return PipelineOptionPage.Unavailable(inventory.ErrorMessage!);

        return PipelineOptionPage.Of(OptionsOf(inventory.Value), request);
    }

    /// <summary>The options this kind offers from the inventory.</summary>
    protected abstract IEnumerable<PipelineOption> OptionsOf(VtsModelInventory inventory);
}
