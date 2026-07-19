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
using NomNomzBot.Infrastructure.Sound.Audio;

namespace NomNomzBot.Infrastructure.Assets.Media;

/// <summary>
/// Content-sniffs asset uploads from their leading bytes — the request's declared content type is never
/// trusted. Covers the asset-library allowlist: png / jpeg / gif / webp / svg+xml for images, and the
/// mp3 / ogg / wav formats via the existing <see cref="AudioSniffer"/>. Zero external dependencies.
/// </summary>
internal static class MediaSniffer
{
    /// <summary>How many leading bytes the sniffer needs (SVG detection reads text, not magic bytes).</summary>
    public const int SampleLength = 512;

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Gif87Magic = "GIF87a"u8.ToArray();
    private static readonly byte[] Gif89Magic = "GIF89a"u8.ToArray();
    private static readonly byte[] RiffMagic = "RIFF"u8.ToArray();
    private static readonly byte[] WebpTag = "WEBP"u8.ToArray();

    /// <summary>The sniffed MIME type, or null when the content matches no allowlisted format.</summary>
    public static string? Sniff(byte[] sample, int length)
    {
        if (length < 4)
            return null;

        if (StartsWith(sample, length, PngMagic))
            return "image/png";
        if (StartsWith(sample, length, JpegMagic))
            return "image/jpeg";
        if (StartsWith(sample, length, Gif87Magic) || StartsWith(sample, length, Gif89Magic))
            return "image/gif";

        // RIFF containers: WEBP (image) and WAVE (audio) share the outer magic — the tag at offset 8 decides.
        if (StartsWith(sample, length, RiffMagic) && length >= 12)
        {
            if (TagAt(sample, 8, WebpTag))
                return "image/webp";
            // WAVE falls through to the audio sniffer below.
        }

        if (LooksLikeSvg(sample, length))
            return "image/svg+xml";

        // Audio formats (mp3 / ogg / wav) — the sound library's sniffer, unchanged.
        byte[] audioHeader = new byte[Math.Min(length, 12)];
        Array.Copy(sample, audioHeader, audioHeader.Length);
        return AudioSniffer.Sniff(audioHeader);
    }

    /// <summary><c>image</c> or <c>audio</c> for an allowlisted MIME type.</summary>
    public static string KindOf(string mimeType) =>
        mimeType.StartsWith("image/", StringComparison.Ordinal) ? "image" : "audio";

    private static bool LooksLikeSvg(byte[] sample, int length)
    {
        // SVG is text: decode the sample leniently, skip BOM/whitespace, and require markup that opens an
        // <svg> root (optionally behind an XML prolog / comments / doctype within the sample window).
        string text;
        try
        {
            text = Encoding.UTF8.GetString(sample, 0, length);
        }
        catch (ArgumentException)
        {
            return false;
        }

        string trimmed = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (!trimmed.StartsWith('<'))
            return false;

        return trimmed.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWith(byte[] sample, int length, byte[] magic)
    {
        if (length < magic.Length)
            return false;
        for (int i = 0; i < magic.Length; i++)
            if (sample[i] != magic[i])
                return false;
        return true;
    }

    private static bool TagAt(byte[] sample, int offset, byte[] tag)
    {
        for (int i = 0; i < tag.Length; i++)
            if (sample[offset + i] != tag[i])
                return false;
        return true;
    }
}
