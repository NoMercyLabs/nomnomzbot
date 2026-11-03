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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Music.Pipeline;

/// <summary>
/// Supplies the playback devices of the channel's music provider for the <c>music_device</c> picker kind. The saved
/// value is the provider's device id; the secondary text is the device type, with "active" added for the device that is
/// playing now. With no usable provider the list is unavailable, with the reason, rather than empty.
/// </summary>
internal sealed class MusicDeviceOptionProvider : IPipelineOptionProvider
{
    private readonly IMusicService _music;

    public MusicDeviceOptionProvider(IMusicService music)
    {
        _music = music;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.MusicDevice;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        string broadcasterKey = broadcasterId.ToString();
        string? reason = await MusicOptionSource.UnavailableReasonAsync(_music, broadcasterKey, ct);
        if (reason is not null)
            return PipelineOptionPage.Unavailable(reason);

        IReadOnlyList<MusicDeviceDto> devices = await _music.GetDevicesAsync(broadcasterKey, ct);
        return PipelineOptionPage.Of(devices.Select(ToOption), request);
    }

    private static PipelineOption ToOption(MusicDeviceDto device) =>
        PipelineOptionPage.Option(
            device.Id,
            device.Name,
            device.IsActive ? $"{device.Type}, active" : device.Type
        );
}
