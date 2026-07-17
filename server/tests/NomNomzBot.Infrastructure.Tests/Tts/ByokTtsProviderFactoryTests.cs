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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Common.Models.Crypto;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Tts.Interfaces;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves the BYOK provider factory (tts.md §3.2): a stored cipher envelope is opened through the vault
/// with the exact AAD it was sealed under (tenant + provider + api_key + key version) and yields a
/// provider adapter of the right type; a channel with no stored key gets NOT_FOUND without touching the
/// vault; a crypto-shredded DEK surfaces KEY_DESTROYED (fails closed, no provider); edge returns the
/// shared key-less adapter; an unknown provider is rejected outright.
/// </summary>
public sealed class ByokTtsProviderFactoryTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e01");
    private static readonly Guid DekId = Guid.Parse("0192a000-0000-7000-8000-000000000e02");

    private static (
        ByokTtsProviderFactory Sut,
        TtsTestDbContext Db,
        ISubjectKeyService SubjectKeys
    ) Build()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        ISubjectKeyService subjectKeys = Substitute.For<ISubjectKeyService>();
        IHttpClientFactory httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());
        EdgeTtsProvider edge = new(new FakeTimeProvider(), NullLogger<EdgeTtsProvider>.Instance);
        ByokTtsProviderFactory sut = new(
            db,
            subjectKeys,
            httpFactory,
            NullLoggerFactory.Instance,
            [edge]
        );
        return (sut, db, subjectKeys);
    }

    private static async Task SeedAzureKeyAsync(TtsTestDbContext db)
    {
        db.TtsConfigs.Add(
            new TtsConfig
            {
                BroadcasterId = Channel,
                SubjectKeyId = DekId,
                AzureApiKeyCipher = "sealed-azure",
                AzureApiKeyNonce = "nonce-a",
                AzureKeyVersion = 1,
                AzureRegion = "westus2",
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_stored_azure_key_opens_under_its_sealing_aad_and_binds_an_azure_adapter()
    {
        (ByokTtsProviderFactory sut, TtsTestDbContext db, ISubjectKeyService keys) = Build();
        await SeedAzureKeyAsync(db);
        keys.UnprotectAsync(
                DekId,
                Arg.Is<CipherPayload>(p => p.CipherText == "sealed-azure" && p.Nonce == "nonce-a"),
                Arg.Is<CipherAad>(a =>
                    a.TenantId == Channel.ToString()
                    && a.Provider == "azure"
                    && a.TokenType == "api_key"
                    && a.KeyVersion == "1"
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success("the-plain-azure-key"));

        Result<ITtsProvider> result = await sut.CreateForChannelAsync(Channel, "azure");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().BeOfType<AzureTtsProvider>();
        // The vault was consulted exactly once with the sealing context — the Arg.Is filters above ARE
        // the assertion that the AAD matches; a mismatched AAD would have left the substitute returning
        // the default (failure) Result.
        await keys.Received(1)
            .UnprotectAsync(
                Arg.Any<Guid>(),
                Arg.Any<CipherPayload>(),
                Arg.Any<CipherAad>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task No_stored_key_is_NOT_FOUND_without_touching_the_vault()
    {
        (ByokTtsProviderFactory sut, _, ISubjectKeyService keys) = Build();

        Result<ITtsProvider> result = await sut.CreateForChannelAsync(Channel, "elevenlabs");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
        await keys.DidNotReceive()
            .UnprotectAsync(
                Arg.Any<Guid>(),
                Arg.Any<CipherPayload>(),
                Arg.Any<CipherAad>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_crypto_shredded_dek_fails_closed_with_KEY_DESTROYED()
    {
        (ByokTtsProviderFactory sut, TtsTestDbContext db, ISubjectKeyService keys) = Build();
        await SeedAzureKeyAsync(db);
        keys.UnprotectAsync(
                Arg.Any<Guid>(),
                Arg.Any<CipherPayload>(),
                Arg.Any<CipherAad>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<string>("The DEK was crypto-shredded.", "KEY_DESTROYED"));

        Result<ITtsProvider> result = await sut.CreateForChannelAsync(Channel, "azure");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("KEY_DESTROYED");
    }

    [Fact]
    public async Task Edge_returns_the_shared_keyless_adapter_and_unknown_is_rejected()
    {
        (ByokTtsProviderFactory sut, _, _) = Build();

        Result<ITtsProvider> edge = await sut.CreateForChannelAsync(Channel, "edge");
        edge.IsSuccess.Should().BeTrue();
        edge.Value.Should().BeOfType<EdgeTtsProvider>();

        Result<ITtsProvider> unknown = await sut.CreateForChannelAsync(Channel, "polly");
        unknown.IsFailure.Should().BeTrue();
        unknown.ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
