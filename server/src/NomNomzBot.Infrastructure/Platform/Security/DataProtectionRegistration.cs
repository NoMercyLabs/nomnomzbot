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

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace NomNomzBot.Infrastructure.Platform.Security;

/// <summary>
/// ASP.NET Core Data Protection with one key ring shared by every process that points at the same data directory.
/// The framework default keeps keys in the container's home folder, so each blue/green colour minted its own ring
/// and anything protected by one colour could not be read by the other after a swap.
/// </summary>
public static class DataProtectionRegistration
{
    public const string ApplicationName = "NomNomzBot";

    public static IServiceCollection AddSharedDataProtection(
        this IServiceCollection services,
        string keysDirectory
    )
    {
        IDataProtectionBuilder builder = services
            .AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));

        // Self-host on Windows seals the ring with the user's DPAPI, matching the OS-vault custody of the KEK. A
        // Linux container keeps it on the root-only data volume, next to the database it protects.
        if (OperatingSystem.IsWindows())
            builder.ProtectKeysWithDpapi();

        return services;
    }
}
