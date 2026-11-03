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
/// Supplies the expression files of the model loaded in the channel's VTube Studio for the <c>vts_expression</c>
/// picker kind. VTS addresses an expression by its file name, so the file name is both the label and the saved value.
/// </summary>
internal sealed class VtsExpressionOptionProvider : VtsInventoryOptionProvider
{
    public VtsExpressionOptionProvider(IVtsControlService vts)
        : base(vts) { }

    public override PipelineActionFieldKind Kind => PipelineActionFieldKind.VtsExpression;

    protected override IEnumerable<PipelineOption> OptionsOf(VtsModelInventory inventory) =>
        inventory.Expressions.Distinct().Select(file => PipelineOptionPage.Option(file, file));
}
