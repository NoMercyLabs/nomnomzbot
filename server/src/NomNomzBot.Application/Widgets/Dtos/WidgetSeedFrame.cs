// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------
namespace NomNomzBot.Application.Widgets.Dtos;

/// <summary>
/// One remembered event a widget gets on join, in the shape of the live event of the same type.
/// <paramref name="OccurredAt"/> is when it really happened.
/// </summary>
public sealed record WidgetSeedFrame(string EventType, object? Data, DateTimeOffset OccurredAt);
