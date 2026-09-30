// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Polly.Telemetry;

namespace NomNomzBot.Infrastructure.Platform.Resilience;

/// <summary>
/// The one severity rule for every resilience pipeline's Polly telemetry. Polly logs a retry attempt at Warning
/// and a timeout or rate-limiter rejection at Error by default, so a normal transient retry floods the log. A
/// retry that recovers is routine and logs at Debug; a timeout, a rate-limiter rejection or an opening circuit
/// breaker is worth seeing and logs at Warning; breaker recovery logs at Information. Any event this rule does
/// not name keeps the severity Polly picked.
/// </summary>
public static class ResilienceTelemetrySeverity
{
    public const string ExecutionAttempt = "ExecutionAttempt";
    public const string Retry = "OnRetry";
    public const string Timeout = "OnTimeout";
    public const string RateLimiterRejected = "OnRateLimiterRejected";
    public const string CircuitOpened = "OnCircuitOpened";
    public const string CircuitHalfOpened = "OnCircuitHalfOpened";
    public const string CircuitClosed = "OnCircuitClosed";

    public static ResilienceEventSeverity Resolve(SeverityProviderArguments arguments) =>
        arguments.Event.EventName switch
        {
            ExecutionAttempt or Retry => ResilienceEventSeverity.Debug,
            Timeout or RateLimiterRejected or CircuitOpened => ResilienceEventSeverity.Warning,
            CircuitHalfOpened or CircuitClosed => ResilienceEventSeverity.Information,
            _ => arguments.Event.Severity,
        };

    /// <summary>Applies <see cref="Resolve"/> to every pipeline built from this service collection. Safe to call
    /// once per resilience handler: each call sets the same delegate.</summary>
    public static IServiceCollection AddResilienceTelemetrySeverity(
        this IServiceCollection services
    ) => services.Configure<TelemetryOptions>(options => options.SeverityProvider = Resolve);
}
