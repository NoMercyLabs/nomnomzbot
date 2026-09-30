// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Infrastructure.Platform.Resilience;
using Polly;
using Polly.Telemetry;

namespace NomNomzBot.Infrastructure.Tests.Platform.Resilience;

/// <summary>
/// A normal transient retry must not flood the log, while an opening circuit breaker must stay visible. Proves
/// the severity rule event by event, then proves it is actually wired into a pipeline built by the REAL
/// <see cref="ResiliencePolicies"/> registration by capturing what Polly writes to the logger.
/// </summary>
public sealed class ResilienceTelemetrySeverityTests
{
    private static SeverityProviderArguments Args(
        string eventName,
        ResilienceEventSeverity polly
    ) =>
        new(
            new ResilienceTelemetrySource("pipeline", "instance", "strategy"),
            new ResilienceEvent(polly, eventName),
            ResilienceContextPool.Shared.Get()
        );

    [Theory]
    [InlineData("ExecutionAttempt", ResilienceEventSeverity.Warning, ResilienceEventSeverity.Debug)]
    [InlineData(
        "ExecutionAttempt",
        ResilienceEventSeverity.Information,
        ResilienceEventSeverity.Debug
    )]
    [InlineData("OnRetry", ResilienceEventSeverity.Warning, ResilienceEventSeverity.Debug)]
    [InlineData("OnTimeout", ResilienceEventSeverity.Error, ResilienceEventSeverity.Warning)]
    [InlineData(
        "OnRateLimiterRejected",
        ResilienceEventSeverity.Error,
        ResilienceEventSeverity.Warning
    )]
    [InlineData("OnCircuitOpened", ResilienceEventSeverity.Error, ResilienceEventSeverity.Warning)]
    [InlineData(
        "OnCircuitHalfOpened",
        ResilienceEventSeverity.Warning,
        ResilienceEventSeverity.Information
    )]
    [InlineData(
        "OnCircuitClosed",
        ResilienceEventSeverity.Warning,
        ResilienceEventSeverity.Information
    )]
    [InlineData("OnHedging", ResilienceEventSeverity.Warning, ResilienceEventSeverity.Warning)]
    [InlineData("SomethingNew", ResilienceEventSeverity.Critical, ResilienceEventSeverity.Critical)]
    public void Resolve_MapsEachEventToItsLevel(
        string eventName,
        ResilienceEventSeverity pollyDefault,
        ResilienceEventSeverity expected
    ) => ResilienceTelemetrySeverity.Resolve(Args(eventName, pollyDefault)).Should().Be(expected);

    private sealed record Entry(string Category, LogLevel Level, string Message);

    private sealed class CapturingProvider(List<Entry> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Capture(categoryName, entries);

        public void Dispose() { }

        private sealed class Capture(string category, List<Entry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                lock (entries)
                    entries.Add(new(category, logLevel, formatter(state, exception)));
            }
        }
    }

    private sealed class ScriptedHandler(Queue<HttpStatusCode> script) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(script.Count > 0 ? script.Dequeue() : HttpStatusCode.OK)
            );
    }

    private static (HttpClient Client, List<Entry> Entries) Build(params HttpStatusCode[] responses)
    {
        List<Entry> entries = [];
        ServiceCollection services = new();
        services.AddLogging(logging =>
            logging.SetMinimumLevel(LogLevel.Trace).AddProvider(new CapturingProvider(entries))
        );
        services
            .AddHttpClient("emotes")
            .ConfigurePrimaryHttpMessageHandler(() => new ScriptedHandler(new(responses)))
            .AddChatEmoteResilienceHandler();
        ServiceProvider provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IHttpClientFactory>().CreateClient("emotes"), entries);
    }

    [Fact]
    public async Task RetryThatRecovers_WritesNothingAboveDebug()
    {
        (HttpClient client, List<Entry> entries) = Build(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK
        );

        HttpResponseMessage response = await client.GetAsync("http://emotes.test/x");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        List<Entry> polly = entries.Where(e => e.Category == "Polly").ToList();
        polly.Should().Contain(e => e.Message.Contains("OnRetry"), "the retry really happened");
        polly.Should().OnlyContain(e => e.Level <= LogLevel.Debug);
    }

    [Fact]
    public async Task CircuitBreakerOpening_WritesAWarning()
    {
        (HttpClient client, List<Entry> entries) = Build(
            Enumerable.Repeat(HttpStatusCode.ServiceUnavailable, 20).ToArray()
        );

        for (int i = 0; i < 3; i++)
        {
            try
            {
                await client.GetAsync("http://emotes.test/x");
            }
            catch (Polly.CircuitBreaker.BrokenCircuitException)
            {
                // Rejected once the breaker is open, which is what the test drives toward.
            }
        }

        Entry opened = entries
            .Where(e => e.Category == "Polly" && e.Message.Contains("OnCircuitOpened"))
            .Should()
            .ContainSingle()
            .Subject;
        opened.Level.Should().Be(LogLevel.Warning);
        entries
            .Where(e => e.Category == "Polly" && e.Message.Contains("OnRetry"))
            .Should()
            .OnlyContain(e => e.Level == LogLevel.Debug);
    }
}
