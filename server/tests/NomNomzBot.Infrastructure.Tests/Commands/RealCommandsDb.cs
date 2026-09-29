// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Billing;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// The PRODUCTION context for command tests that depend on soft delete: the soft-delete interceptor, the
/// global soft-delete filter and the real unique indexes. <see cref="CommandsTestDbContext"/> has none of
/// them, so a deleted-row bug can never reproduce against it. One channel is seeded.
/// </summary>
internal sealed class RealCommandsDb : IDisposable
{
    public static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000c501");
    public static readonly Guid Owner = Guid.Parse("0192a000-0000-7000-8000-00000000c500");

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public RealCommandsDb()
    {
        _connection.Open();
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(
                new SoftDeleteInterceptor(TimeProvider.System, new NullCurrentUserService())
            )
            .Options;
        Db = new(options);
        Db.Database.EnsureCreated();

        Db.Users.Add(
            new()
            {
                Id = Owner,
                TwitchUserId = "tw-owner-c5",
                Username = "reuse",
                UsernameNormalized = "reuse",
                DisplayName = "Reuse",
            }
        );
        Db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Owner,
                TwitchChannelId = "tw-channel-c5",
                Name = "reuse",
                NameNormalized = "reuse",
            }
        );
        Db.SaveChanges();

        Commands = new(
            Db,
            Substitute.For<IPipelineEngine>(),
            Registry,
            Bus,
            TestQuota.Unlimited(),
            new TemplateHelperValidator()
        );
        Presets = new(Commands, Db, Registry, Bus, new TemplateHelperValidator());
    }

    public AppDbContext Db { get; }
    public IChannelRegistry Registry { get; } = Substitute.For<IChannelRegistry>();
    public RecordingEventBus Bus { get; } = new();
    public CommandService Commands { get; }
    public CommandPresetService Presets { get; }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
