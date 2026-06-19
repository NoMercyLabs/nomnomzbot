// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Events;

public sealed class PermissionChangedEvent : DomainEventBase
{
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string ResourceType { get; init; }
    public required string ResourceId { get; init; }
    public required int NewPermissionValue { get; init; }
}
