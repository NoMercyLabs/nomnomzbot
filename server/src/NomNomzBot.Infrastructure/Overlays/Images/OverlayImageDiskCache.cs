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

using System.Security.Cryptography;
using System.Text;
using NomNomzBot.Application.Overlays.Services;

namespace NomNomzBot.Infrastructure.Overlays.Images;

/// <summary>
/// The converted WebP files, one per (source URL, width bucket), named by the SHA-256 of that pair. When the folder
/// grows past its cap the least recently used files go first; a read counts as a use. The file just written is
/// never the one dropped.
/// </summary>
public sealed class OverlayImageDiskCache
{
    public const long DefaultMaxBytes = 512L * 1024 * 1024;

    private const string Extension = ".webp";

    private readonly string _directory;
    private readonly long _maxBytes;
    private readonly Lock _evictionLock = new();

    public OverlayImageDiskCache(string directory, long maxBytes = DefaultMaxBytes)
    {
        _directory = directory;
        _maxBytes = maxBytes;
        Directory.CreateDirectory(directory);
    }

    public OptimizedImage? TryGet(string source, int bucket)
    {
        string key = KeyOf(source, bucket);
        string path = PathOf(key);
        if (!File.Exists(path))
            return null;

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        return new OptimizedImage(path, new FileInfo(path).Length, ETagOf(key));
    }

    public OptimizedImage Put(string source, int bucket, byte[] webp)
    {
        string key = KeyOf(source, bucket);
        string path = PathOf(key);
        string partial = Path.Combine(_directory, $"{key}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(partial, webp);
        File.Move(partial, path, overwrite: true);

        Evict(keep: path);
        return new OptimizedImage(path, webp.Length, ETagOf(key));
    }

    private void Evict(string keep)
    {
        lock (_evictionLock)
        {
            List<FileInfo> files = new DirectoryInfo(_directory)
                .EnumerateFiles($"*{Extension}")
                .OrderBy(file => file.LastWriteTimeUtc)
                .ToList();
            long total = files.Sum(file => file.Length);

            foreach (FileInfo file in files)
            {
                if (total <= _maxBytes)
                    return;
                if (file.FullName == keep)
                    continue;

                total -= file.Length;
                file.Delete();
            }
        }
    }

    private string PathOf(string key) => Path.Combine(_directory, key + Extension);

    private static string KeyOf(string source, int bucket) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{source}\n{bucket}")));

    // The ETag is the file name's first 32 hex digits: the same pair always yields the same tag.
    private static string ETagOf(string key) => $"\"{key[..32]}\"";
}
