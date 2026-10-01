// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Application.Widgets.Services;

/// <summary>
/// Runs one registered pipeline action on behalf of a widget, as the channel owner (widget-sdk.md §8). The only
/// gate is the owner's own IAM — the same check the dashboard's pipeline test-run passes — so a widget can do
/// exactly what its owner can do, and nothing more.
/// </summary>
public interface IWidgetActionService
{
    /// <summary>
    /// Fails with <c>NOT_FOUND</c> for a widget outside the channel or an unknown action type, <c>FORBIDDEN</c>
    /// for a turned-off widget or an owner without the permission, and <c>RATE_LIMITED</c> for a widget that
    /// calls too often. A run that starts always returns an outcome, also when the action itself fails.
    /// </summary>
    Task<Result<WidgetActionOutcome>> InvokeAsync(
        WidgetActionRequest request,
        CancellationToken cancellationToken = default
    );
}
