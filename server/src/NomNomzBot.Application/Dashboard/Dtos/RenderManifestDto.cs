// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.DTOs.Twitch;
using NomNomzBot.Application.Integrations.Dtos;
using NomNomzBot.Application.Platform.Dtos;

namespace NomNomzBot.Application.Dashboard.Dtos;

/// <summary>
/// Everything the dashboard shell needs to render for the active channel, aggregated into one
/// response so the client boots with a single request instead of four. Each section reuses the same
/// DTO its dedicated endpoint returns:
/// <list type="bullet">
///   <item><description><see cref="Access"/> — the caller's resolved access (<c>roles/effective/me</c>).</description></item>
///   <item><description><see cref="Features"/> — the channel's feature toggles (<c>features</c>).</description></item>
///   <item><description><see cref="Integrations"/> — integration connection states (<c>integrations</c>).</description></item>
///   <item><description><see cref="Scopes"/> — outstanding Twitch scope gaps (<c>twitch/diagnostics/missing-scopes</c>).</description></item>
/// </list>
/// <para>
/// <see cref="Access"/> is always resolved (self-introspection) and is load-bearing. The feature,
/// integration, and scope sections are each populated ONLY when the caller clears that surface's
/// Gate-2 read floor (per their <see cref="ResolvedAccessDto.HeldActionKeys"/>) — a participant sees
/// them empty — so the aggregate never leaks data an individual endpoint would have withheld. Of the
/// entitled sections, features is load-bearing (a real failure fails the manifest); integrations and
/// scopes degrade to an empty section instead.
/// </para>
/// </summary>
public sealed record RenderManifestDto(
    ResolvedAccessDto Access,
    IReadOnlyList<FeatureStatusDto> Features,
    IReadOnlyList<ChannelIntegrationDto> Integrations,
    MissingScopesDto Scopes
);
