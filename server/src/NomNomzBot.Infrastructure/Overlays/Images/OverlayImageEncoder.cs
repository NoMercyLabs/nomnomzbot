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

using System.Text;
using NetVips;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Overlays.Services;
using NvImage = NetVips.Image;

namespace NomNomzBot.Infrastructure.Overlays.Images;

/// <summary>
/// Resizes image bytes to a WebP no wider than the bucket (never larger than the source). An animated GIF or WebP
/// is loaded with all its frames, so the output keeps every frame and the loop.
/// </summary>
internal static class OverlayImageEncoder
{
    private const long MaxPixelsAcrossFrames = 150_000_000;
    private const int WebpQuality = 80;

    // libvips fits the image inside width x height; a tall image must not be squeezed by the height.
    private const int NoHeightLimit = 1_000_000;

    public static Result<byte[]> ToWebp(byte[] source, int bucket)
    {
        if (LooksLikeSvg(source))
            return Unsupported("SVG images are not resized here.");

        try
        {
            string loadOptions = LoadOptions(source);
            using NvImage probe = NvImage.NewFromBuffer(source, loadOptions);
            if ((long)probe.Width * probe.Height > MaxPixelsAcrossFrames)
                return Unsupported("The image has too many pixels.");

            using NvImage resized = NvImage.ThumbnailBuffer(
                source,
                bucket,
                optionString: loadOptions,
                height: NoHeightLimit,
                size: Enums.Size.Down
            );
            return Result.Success(resized.WebpsaveBuffer(q: WebpQuality));
        }
        catch (VipsException)
        {
            return Unsupported("The bytes are not an image that can be resized.");
        }
    }

    // Only the GIF and WebP loaders take "n" (frames to load; -1 = all); the others reject the option.
    private static string LoadOptions(byte[] source) =>
        IsGif(source) || IsWebp(source) ? "n=-1" : string.Empty;

    private static bool IsGif(byte[] source) => source.AsSpan().StartsWith("GIF8"u8);

    private static bool IsWebp(byte[] source) =>
        source.Length > 12
        && source.AsSpan(0, 4).SequenceEqual("RIFF"u8)
        && source.AsSpan(8, 4).SequenceEqual("WEBP"u8);

    private static bool LooksLikeSvg(byte[] source)
    {
        string head = Encoding
            .UTF8.GetString(source, 0, Math.Min(source.Length, 2048))
            .TrimStart('﻿', ' ', '\t', '\r', '\n');
        return head.StartsWith('<') && head.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    private static Result<byte[]> Unsupported(string message) =>
        Result.Failure<byte[]>(message, IOverlayImageOptimizer.ErrorCodes.Unsupported);
}
