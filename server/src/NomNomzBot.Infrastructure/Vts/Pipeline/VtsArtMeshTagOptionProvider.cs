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
/// Supplies the art mesh tags of the model loaded in the channel's VTube Studio for the <c>vts_art_mesh_tag</c> picker
/// kind (VTS <c>ArtMeshListRequest</c> returns them as <c>artMeshTags</c>). The saved value is the tag. With no model
/// loaded the list is empty.
/// </summary>
internal sealed class VtsArtMeshTagOptionProvider : VtsInventoryOptionProvider
{
    public VtsArtMeshTagOptionProvider(IVtsControlService vts)
        : base(vts) { }

    public override PipelineActionFieldKind Kind => PipelineActionFieldKind.VtsArtMeshTag;

    protected override IEnumerable<PipelineOption> OptionsOf(VtsModelInventory inventory) =>
        inventory.ArtMeshTags.Select(t => PipelineOptionPage.Option(t, t));
}
