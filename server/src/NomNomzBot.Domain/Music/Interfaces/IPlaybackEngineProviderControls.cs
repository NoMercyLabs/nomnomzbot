// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Domain.Music.Interfaces;

/// <summary>
/// The provider controls that are not director inputs: shuffle, repeat, devices, device transfer and the
/// embedded player token. An engine without one answers <see cref="EngineControlOutcome.NotSupported"/>
/// instead of doing nothing; the dashboard gates on <see cref="Capabilities"/>.
/// </summary>
public interface IPlaybackEngineProviderControls
{
    MusicProviderCapabilities Capabilities { get; }

    Task<EngineControlResult> SetShuffleAsync(bool enabled, CancellationToken ct = default);

    Task<EngineControlResult> SetRepeatAsync(MusicRepeatMode mode, CancellationToken ct = default);

    Task<EngineControlResult<IReadOnlyList<MusicDeviceInfo>>> GetDevicesAsync(
        CancellationToken ct = default
    );

    Task<EngineControlResult> TransferAsync(
        string deviceId,
        bool play,
        CancellationToken ct = default
    );

    Task<EngineControlResult<string>> GetEmbeddedTokenAsync(CancellationToken ct = default);
}
