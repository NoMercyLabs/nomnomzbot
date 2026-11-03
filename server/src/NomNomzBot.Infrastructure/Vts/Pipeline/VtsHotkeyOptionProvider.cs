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
/// Supplies the hotkeys of the model loaded in the channel's VTube Studio for the <c>vts_hotkey</c> picker kind. The
/// saved value is the VTS hotkey id; the secondary text is the hotkey type VTS reports. VTS lists hotkeys only for a
/// loaded model, so with no model loaded the list is empty.
/// </summary>
internal sealed class VtsHotkeyOptionProvider : VtsInventoryOptionProvider
{
    public VtsHotkeyOptionProvider(IVtsControlService vts)
        : base(vts) { }

    public override PipelineActionFieldKind Kind => PipelineActionFieldKind.VtsHotkey;

    protected override IEnumerable<PipelineOption> OptionsOf(VtsModelInventory inventory) =>
        inventory.Hotkeys.Select(h => PipelineOptionPage.Option(h.Id, h.Name, h.Type));
}
