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
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Application.Vts.Dtos;
using NomNomzBot.Application.Vts.Services;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Vts.Pipeline;

/// <summary>
/// Supplies the models of the channel's connected VTube Studio for the <c>vts_model</c> picker kind. The saved value
/// is the VTS model id; the model that is loaded now is marked in the secondary text.
/// </summary>
internal sealed class VtsModelOptionProvider : VtsInventoryOptionProvider
{
    public VtsModelOptionProvider(IVtsControlService vts)
        : base(vts) { }

    public override PipelineActionFieldKind Kind => PipelineActionFieldKind.VtsModel;

    protected override IEnumerable<PipelineOption> OptionsOf(VtsModelInventory inventory) =>
        inventory.Models.Select(m =>
            PipelineOptionPage.Option(m.Id, m.Name, m.IsLoaded ? "loaded" : null)
        );
}
