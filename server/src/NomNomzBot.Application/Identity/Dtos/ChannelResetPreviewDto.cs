// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Consequences;

namespace NomNomzBot.Application.Identity.Dtos;

/// <summary>
/// What resetting a channel to defaults clears, counted BEFORE the operator confirms it. A reset is not
/// reversible and has no restore window, so unlike <see cref="ChannelDeletePreviewDto"/> it carries no date.
/// </summary>
/// <param name="ChannelName">The channel's real name, shown so the operator sees which channel is reset.</param>
/// <param name="BlastRadius">
/// The configuration rows the reset removes, grouped by settings area. The groups come from the same scope
/// the reset itself deletes through, so the preview cannot list more or less than the reset removes.
/// </param>
public sealed record ChannelResetPreviewDto(string ChannelName, BlastRadiusDto BlastRadius);
