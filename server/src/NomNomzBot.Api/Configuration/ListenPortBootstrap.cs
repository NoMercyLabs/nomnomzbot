// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NomNomzBot.Domain.Enums.Deployment;

namespace NomNomzBot.Api.Configuration;

/// <summary>
/// Boot wiring for smart self-host port handling (deployment-distribution §6). Self-host only: reads the preferred
/// port from the configured <c>Urls</c>, resolves the actual port to bind, rewrites <c>Urls</c> so Kestrel binds it,
/// and points <c>App:BaseUrl</c> at it (when no explicit external URL is set) so the OAuth redirect URLs match the
/// real port. Whatever it binds on the first boot is <b>locked</b> via the <see cref="ILockedPortStore"/>; later boots
/// reuse that exact port, reclaiming it from a stale copy of the bot but aborting (rather than silently moving) if an
/// unrelated app holds it — moving would invalidate the registered OAuth redirect URLs. SaaS keeps its configured
/// binding untouched — the operator's reverse proxy owns the port there and there is no LAN to step aside for.
/// </summary>
public static class ListenPortBootstrap
{
    /// <summary>
    /// Resolve and apply the self-host listen port. Returns the resolved port (or <c>null</c> on SaaS / when no
    /// loopback <c>Urls</c> port is configured, meaning the binding is left exactly as-is). The bootstrap logger is
    /// used because this runs before the host — and therefore the DI logger — exists. <paramref name="lockStore"/> and
    /// <paramref name="resolver"/> default to the real file-backed store and OS resolver; tests inject fakes.
    /// </summary>
    /// <exception cref="ListenPortLockedException">
    /// The install's locked port is held by an unrelated application; boot is aborted rather than moving to a port the
    /// OAuth redirect URLs are not registered against.
    /// </exception>
    public static int? ResolveAndApply(
        IConfiguration configuration,
        DeploymentMode mode,
        ILogger logger,
        ILockedPortStore? lockStore = null,
        IListenPortResolver? resolver = null
    )
    {
        // SaaS binds behind a proxy on a fixed port the operator controls; never auto-shift it.
        if (mode is not (DeploymentMode.SelfHostLite or DeploymentMode.SelfHostFull))
            return null;

        string? urls = configuration["Urls"] ?? configuration["urls"];
        if (!TryGetLoopbackPort(urls, out int preferredPort))
        {
            // No simple loopback Urls port (e.g. bound to a domain/0.0.0.0 or ASPNETCORE_URLS set elsewhere) — leave
            // the binding untouched; smart shifting only applies to the local self-host loopback listener.
            return null;
        }

        lockStore ??= new FileLockedPortStore();
        resolver ??= new ListenPortResolver(
            new SystemPortOperations(),
            new TypedLoggerAdapter<ListenPortResolver>(logger)
        );

        int? lockedPort = lockStore.Read();
        ListenPortDecision decision = resolver.Resolve(preferredPort, lockedPort);

        if (decision.IsConflict)
        {
            // Cannot bind the committed port and must not move. Abort with a clear, actionable message — surfaced to the
            // operator (a dialog on the windowless single binary) by the top-level startup handler.
            logger.LogCritical("{Message}", decision.Describe());
            throw new ListenPortLockedException(decision.Describe());
        }

        if (
            decision.Reason is PortResolution.PreferredFree or PortResolution.HonoredLock
            && decision.Port == decision.PreferredPort
        )
            logger.LogInformation("{Message}", decision.Describe());
        else
            logger.LogWarning("{Message}", decision.Describe());

        configuration["Urls"] = $"http://localhost:{decision.Port}";

        // Point the OAuth redirect URLs at the actual bound port, unless an explicit external base URL (a domain or
        // tunnel the operator fronts the bot with) is configured — that one owns the public URL and must win.
        string? baseUrl = configuration["App:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl) || IsLoopback(baseUrl))
            configuration["App:BaseUrl"] = $"http://localhost:{decision.Port}";

        // First boot (nothing locked yet): commit to whatever we bound so the port — and the OAuth redirect URLs that
        // will be registered against it — stay stable across every later restart.
        if (lockedPort is null)
            lockStore.Write(decision.Port);

        return decision.Port;
    }

    /// <summary>True if <paramref name="url"/> parses to a loopback host (<c>localhost</c> / <c>127.0.0.1</c>).</summary>
    private static bool IsLoopback(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && (
            string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host == "127.0.0.1"
        );

    /// <summary>
    /// Extract a single loopback port from a (possibly semicolon-separated) <c>Urls</c> value. Returns the port of
    /// the first <c>http://localhost</c> / <c>127.0.0.1</c> entry. Anything else (a real host, https, a wildcard
    /// bind) is left to the host as-is.
    /// </summary>
    public static bool TryGetLoopbackPort(string? urls, out int port)
    {
        port = 0;
        if (string.IsNullOrWhiteSpace(urls))
            return false;

        foreach (
            string candidate in urls.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri))
                continue;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                continue;
            bool isLoopback =
                string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || uri.Host == "127.0.0.1";
            if (!isLoopback)
                continue;
            if (uri.Port <= 0)
                continue;

            port = uri.Port;
            return true;
        }

        return false;
    }

    /// <summary>Adapts the non-generic bootstrap <see cref="ILogger"/> to the generic <c>ILogger&lt;T&gt;</c> the resolver expects.</summary>
    private sealed class TypedLoggerAdapter<T>(ILogger inner) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
