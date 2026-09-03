// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Platform.Dtos;
using NomNomzBot.Infrastructure.Platform.Configuration;
using NomNomzBot.Infrastructure.Tests.Content;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform;

/// <summary>
/// The operator console's provider-credential surface.
///
/// <para>Two properties carry the weight. First, <b>a secret never comes back out</b>: the DTO has no field
/// for one, and the read path reports only whether one exists — a surface that echoed a secret back into a
/// dashboard would be a worse leak than the missing feature it replaced.</para>
///
/// <para>Second, <b>the reported SOURCE has to be the one that wins</b>. A stored value shadows the
/// environment, and this instance has already lost hours to a stale stored secret quietly beating a
/// corrected env var — 401s that read as a revoked app. If this surface named the wrong source, it would
/// send the operator to fix the value that was being ignored.</para>
/// </summary>
public sealed class ProviderCredentialServiceTests
{
    /// <summary>
    /// A protector that seals reversibly and honours the AAD. Sealing is asserted through it rather than
    /// mocked away because the sealed value has to be openable BY THE RESOLVER under the same AAD — a fake
    /// that ignored the context would hide exactly the mismatch that matters.
    /// </summary>
    private sealed class ReversibleProtector : ITokenProtector
    {
        public Task<string> ProtectAsync(
            string plaintext,
            TokenProtectionContext context,
            CancellationToken ct = default
        ) => Task.FromResult($"sealed[{Aad(context)}]:{plaintext}");

        public Task<string?> TryUnprotectAsync(
            string? sealedValue,
            TokenProtectionContext context,
            CancellationToken ct = default
        )
        {
            string prefix = $"sealed[{Aad(context)}]:";
            return Task.FromResult(
                sealedValue is not null && sealedValue.StartsWith(prefix, StringComparison.Ordinal)
                    ? sealedValue[prefix.Length..]
                    : null
            );
        }

        private static string Aad(TokenProtectionContext context) =>
            $"{context.SubjectId}|{context.Provider}|{context.Field}";
    }

    private static (ProviderCredentialService Service, SeedTestDbContext Db) Build(
        Dictionary<string, string?>? environment = null
    )
    {
        SeedTestDbContext db = SeedTestDbContext.New();

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(environment ?? [])
            .Build();

        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        credentials
            .IsAppDecisionRecordedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        return (new(db, configuration, new ReversibleProtector(), credentials), db);
    }

    private static ProviderCredentialDto Row(
        IReadOnlyList<ProviderCredentialDto> rows,
        string provider
    ) => rows.Single(r => r.Provider == provider);

    [Fact]
    public async Task Every_provider_the_build_supports_is_listed_exactly_once()
    {
        // Enumerated from the catalogue, not a hand-kept list: a provider added to the product and forgotten
        // here would be unmanageable, which is the failure this whole surface exists to end.
        (ProviderCredentialService service, _) = Build();

        Result<IReadOnlyList<ProviderCredentialDto>> result = await service.ListAsync();

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Select(r => r.Provider)
            .Should()
            .BeEquivalentTo(ProviderCredentialService.Providers);
        result
            .Value.Select(r => r.Provider)
            .Should()
            .OnlyHaveUniqueItems("a duplicated provider row would let one edit silently overwrite another");
    }

    [Fact]
    public async Task An_unconfigured_provider_reports_both_fields_unset()
    {
        (ProviderCredentialService service, _) = Build();

        ProviderCredentialDto twitch = Row((await service.ListAsync()).Value, "twitch");

        twitch.ClientId.Should().BeNull();
        twitch.ClientIdSource.Should().Be(CredentialSource.Unset);
        twitch.SecretSource.Should().Be(CredentialSource.Unset);
    }

    [Fact]
    public async Task An_environment_credential_is_reported_as_coming_from_the_environment()
    {
        (ProviderCredentialService service, _) = Build(
            new()
            {
                ["Twitch:ClientId"] = "env-client-id",
                ["Twitch:ClientSecret"] = "env-secret",
            }
        );

        ProviderCredentialDto twitch = Row((await service.ListAsync()).Value, "twitch");

        twitch.ClientId.Should().Be("env-client-id");
        twitch.ClientIdSource.Should().Be(CredentialSource.Environment);
        twitch.SecretSource.Should().Be(CredentialSource.Environment);
    }

    [Fact]
    public async Task A_stored_credential_shadows_the_environment_and_says_so()
    {
        // The failure that motivated the surface: the operator fixes a rotated secret in .env, keeps getting
        // 401s, and cannot see the stale stored value that is actually being sent.
        (ProviderCredentialService service, _) = Build(
            new()
            {
                ["Twitch:ClientId"] = "env-client-id",
                ["Twitch:ClientSecret"] = "env-secret",
            }
        );

        await service.SaveAsync("twitch", new("stored-client-id", "stored-secret"));

        ProviderCredentialDto twitch = Row((await service.ListAsync()).Value, "twitch");
        twitch.ClientId.Should().Be("stored-client-id", "the stored value is the one being sent");
        twitch.ClientIdSource.Should().Be(CredentialSource.Stored);
        twitch.SecretSource.Should().Be(CredentialSource.Stored);
    }

    [Fact]
    public async Task No_read_path_ever_returns_a_secret()
    {
        (ProviderCredentialService service, _) = Build(
            new() { ["Twitch:ClientSecret"] = "env-secret" }
        );
        await service.SaveAsync("twitch", new(null, "stored-secret"));

        IReadOnlyList<ProviderCredentialDto> rows = (await service.ListAsync()).Value;

        // Asserted across the WHOLE serialized surface, not field by field: a secret added to the DTO later
        // would slip past a per-field check.
        string serialized = System.Text.Json.JsonSerializer.Serialize(rows);
        serialized.Should().NotContain("stored-secret");
        serialized.Should().NotContain("env-secret");
    }

    [Fact]
    public async Task The_stored_secret_is_sealed_under_the_resolvers_own_AAD()
    {
        // If this service sealed under a different AAD, the value would save fine and then fail to open in
        // the OAuth flow — a break that only shows up at login time.
        (ProviderCredentialService service, SeedTestDbContext db) = Build();

        await service.SaveAsync("twitch", new(null, "stored-secret"));

        Domain.Platform.Entities.Configuration row = db.Configurations.Single(c => c.Key == "twitch.client_secret");
        row.Value.Should().BeNull("a sealed secret must never sit beside a plaintext copy");
        row.SecureValue.Should().NotBeNullOrEmpty();

        TokenProtectionContext expected = SystemCredentialsProvider.ContextFor(
            "twitch.client_secret"
        );
        string? opened = await new ReversibleProtector().TryUnprotectAsync(
            row.SecureValue!,
            expected
        );
        opened.Should().Be("stored-secret");
    }

    [Fact]
    public async Task Sealing_a_secret_removes_any_plaintext_that_was_sitting_in_the_same_row()
    {
        // A row that already held the value in the clear — an older build, or a hand-edited database. The
        // sealed value is what the resolver prefers, so a leftover plaintext is not merely untidy: it is the
        // secret still readable to anyone with a database connection, in a row the operator was just told is
        // now sealed. (Found by mutation: dropping the clear left every other assertion green.)
        (ProviderCredentialService service, SeedTestDbContext db) = Build();
        db.Configurations.Add(
            new()
            {
                BroadcasterId = null,
                Key = "twitch.client_secret",
                Value = "plaintext-from-an-older-build",
            }
        );
        await db.SaveChangesAsync();

        await service.SaveAsync("twitch", new(null, "freshly-sealed"));

        Domain.Platform.Entities.Configuration row = db.Configurations.Single(c =>
            c.Key == "twitch.client_secret"
        );
        row.Value.Should().BeNull("the plaintext must not survive alongside the sealed value");
        (
            await new ReversibleProtector().TryUnprotectAsync(
                row.SecureValue!,
                SystemCredentialsProvider.ContextFor("twitch.client_secret")
            )
        )
            .Should()
            .Be("freshly-sealed");
    }

    [Fact]
    public async Task A_secret_sealed_for_one_provider_cannot_be_opened_as_another()
    {
        (ProviderCredentialService service, SeedTestDbContext db) = Build();

        await service.SaveAsync("twitch", new(null, "twitch-secret"));

        Domain.Platform.Entities.Configuration row = db.Configurations.Single(c => c.Key == "twitch.client_secret");
        string? asSpotify = await new ReversibleProtector().TryUnprotectAsync(
            row.SecureValue!,
            SystemCredentialsProvider.ContextFor("spotify.client_secret")
        );

        asSpotify.Should().BeNull("the AAD binds a sealed value to its own provider and field");
    }

    [Fact]
    public async Task Rotating_the_secret_alone_leaves_the_client_id_untouched()
    {
        (ProviderCredentialService service, _) = Build();
        await service.SaveAsync("twitch", new("keep-this-id", "old-secret"));

        await service.SaveAsync("twitch", new(null, "new-secret"));

        ProviderCredentialDto twitch = Row((await service.ListAsync()).Value, "twitch");
        twitch.ClientId.Should().Be("keep-this-id");
        twitch.SecretSource.Should().Be(CredentialSource.Stored);
    }

    [Fact]
    public async Task A_blank_field_leaves_the_stored_value_alone_rather_than_clearing_it()
    {
        // A half-filled form must never wipe a working credential. Clearing is a separate verb.
        (ProviderCredentialService service, _) = Build();
        await service.SaveAsync("twitch", new("keep-this-id", "keep-this-secret"));

        await service.SaveAsync("twitch", new("   ", "new-secret"));

        Row((await service.ListAsync()).Value, "twitch").ClientId.Should().Be("keep-this-id");
    }

    [Fact]
    public async Task Saving_nothing_at_all_is_rejected_instead_of_silently_succeeding()
    {
        (ProviderCredentialService service, _) = Build();

        Result<ProviderCredentialDto> result = await service.SaveAsync("twitch", new(null, null));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Clearing_hands_resolution_back_to_the_environment()
    {
        (ProviderCredentialService service, SeedTestDbContext db) = Build(
            new()
            {
                ["Twitch:ClientId"] = "env-client-id",
                ["Twitch:ClientSecret"] = "env-secret",
            }
        );
        await service.SaveAsync("twitch", new("stored-client-id", "stored-secret"));

        Result<ProviderCredentialDto> cleared = await service.ClearAsync("twitch");

        cleared.IsSuccess.Should().BeTrue();
        cleared.Value.ClientId.Should().Be("env-client-id");
        cleared.Value.ClientIdSource.Should().Be(CredentialSource.Environment);
        cleared.Value.SecretSource.Should().Be(CredentialSource.Environment);

        // Rows are really gone, not soft-deleted: a soft-deleted row the resolver still reads would leave
        // the operator staring at "cleared" while the stale secret kept winning.
        db.Configurations.Where(c => c.Key.StartsWith("twitch.")).Should().BeEmpty();
    }

    [Fact]
    public async Task Clearing_one_provider_leaves_the_others_stored_credentials_alone()
    {
        (ProviderCredentialService service, _) = Build();
        await service.SaveAsync("twitch", new("twitch-id", "twitch-secret"));
        await service.SaveAsync("spotify", new("spotify-id", "spotify-secret"));

        await service.ClearAsync("twitch");

        IReadOnlyList<ProviderCredentialDto> rows = (await service.ListAsync()).Value;
        Row(rows, "twitch").ClientIdSource.Should().Be(CredentialSource.Unset);
        Row(rows, "spotify").ClientId.Should().Be("spotify-id");
        Row(rows, "spotify").SecretSource.Should().Be(CredentialSource.Stored);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("")]
    public async Task An_unknown_provider_is_refused_by_both_write_paths(string provider)
    {
        (ProviderCredentialService service, _) = Build();

        (await service.SaveAsync(provider, new("id", "secret"))).IsFailure.Should().BeTrue();
        (await service.ClearAsync(provider)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task A_sealed_secret_that_will_not_open_reads_as_absent_rather_than_configured()
    {
        // Exactly what an ENCRYPTION_KEY rotation leaves behind. The resolver treats it as absent, so
        // reporting "configured" here would send the operator hunting a value that cannot be used.
        (ProviderCredentialService service, SeedTestDbContext db) = Build();
        db.Configurations.Add(
            new()
            {
                BroadcasterId = null,
                Key = "twitch.client_secret",
                SecureValue = "sealed[system|other|thing]:unreadable",
            }
        );
        await db.SaveChangesAsync();

        Row((await service.ListAsync()).Value, "twitch")
            .SecretSource.Should()
            .Be(CredentialSource.Unset);
    }
}
