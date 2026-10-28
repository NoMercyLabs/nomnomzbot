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
