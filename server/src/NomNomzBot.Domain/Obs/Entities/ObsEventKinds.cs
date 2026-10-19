// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Obs.Entities;

/// <summary>
/// The OBS-WebSocket v5 event categories, each at its protocol bit (obs-control.md §6). Bits 0–10 are the
/// general categories; bits 16–19 are the high-volume opt-ins.
/// </summary>
public static class ObsEventKinds
{
    public static readonly IReadOnlyList<(string Name, int Bit)> All =
    [
        ("General", 1 << 0),
        ("Config", 1 << 1),
        ("Scenes", 1 << 2),
        ("Inputs", 1 << 3),
        ("Transitions", 1 << 4),
        ("Filters", 1 << 5),
        ("Outputs", 1 << 6),
        ("SceneItems", 1 << 7),
        ("MediaInputs", 1 << 8),
        ("Vendors", 1 << 9),
        ("Ui", 1 << 10),
        ("InputVolumeMeters", 1 << 16),
        ("InputActiveStateChanged", 1 << 17),
        ("InputShowStateChanged", 1 << 18),
        ("SceneItemTransformChanged", 1 << 19),
    ];
}
