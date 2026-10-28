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

namespace NomNomzBot.Application.Widgets.Dtos;

/// <summary>
/// One remembered event a widget gets on join, in the shape of the live event of the same type.
/// <paramref name="OccurredAt"/> is when it really happened.
/// </summary>
public sealed record WidgetSeedFrame(string EventType, object? Data, DateTimeOffset OccurredAt);
