// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// A short sine beep as a <c>data:audio/wav</c> URI, the playback URL of the editor's play_sound test sample. It is
/// generated here so the sample points at real audio that needs no host and no network; the editor CSP allows
/// <c>data:</c> in <c>media-src</c>.
/// </summary>
internal static class WidgetTestBeep
{
    private const int SampleRate = 8000;
    private const int FrequencyHz = 880;
    private const int DurationMs = 250;
    private const short Amplitude = 8000;

    public static readonly string DataUri = Build();

    private static string Build()
    {
        int sampleCount = SampleRate * DurationMs / 1000;
        int dataBytes = sampleCount * 2;

        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.ASCII);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        for (int i = 0; i < sampleCount; i++)
            writer.Write((short)(Amplitude * Math.Sin(2 * Math.PI * FrequencyHz * i / SampleRate)));
        writer.Flush();

        return "data:audio/wav;base64," + Convert.ToBase64String(stream.ToArray());
    }
}
