// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Acornima.Ast;
using Jint;
using Jint.Native;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Widgets.Bundling;

/// <summary>
/// Compiles Vue SFCs by running the vendored <c>@vue/compiler-sfc</c> bundle (+ our <c>compile-sfc.js</c> wrapper)
/// inside Jint. Parsing the ~778 kb bundle is expensive (~1.6–2.9 s once), so this is a <b>singleton</b> that
/// prepares the scripts once and keeps a small pool of pre-warmed engines — a Jint <see cref="Engine"/> is
/// single-threaded/non-reentrant, so each compile rents one engine under a semaphore and returns it; warm compiles
/// are ~50–110 ms. A compile error is a coded <see cref="Result"/> failure (never a thrown exception).
/// </summary>
public sealed class JintVueSfcCompiler : IVueSfcCompiler, IDisposable
{
    /// <summary>The stable coded failure for any Vue compile problem — parse, script, template, or style.</summary>
    public const string CompileFailedCode = "WIDGET_VUE_COMPILE_FAILED";

    private const string BundleResourceName =
        "NomNomzBot.Infrastructure.Widgets.Vendor.vue-compiler-sfc.js";
    private const string WrapperResourceName =
        "NomNomzBot.Infrastructure.Widgets.Vendor.compile-sfc.js";

    // Loads the vendored bundle onto `VueCompilerSFC`, then our wrapper onto `__compileSfc`. The console shim is
    // the only polyfill the browser build of @vue/compiler-sfc needs.
    private const string ConsoleShim =
        "var console={log:__sink,warn:__sink,error:__sink,info:__sink,debug:__sink,trace:__sink};";
    private const string PromoteGlobal =
        "if(typeof VueCompilerSFC!=='undefined'){globalThis.VueCompilerSFC=VueCompilerSFC;}";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<JintVueSfcCompiler> _logger;
    private readonly Prepared<Script> _preparedBundle;
    private readonly Prepared<Script> _preparedWrapper;
    private readonly SemaphoreSlim _gate;
    private readonly ConcurrentQueue<Engine> _pool = new();
    private readonly int _maxEngines;
    private int _createdEngines;
    private bool _disposed;

    public JintVueSfcCompiler(IConfiguration configuration, ILogger<JintVueSfcCompiler> logger)
    {
        _logger = logger;

        int configured = int.TryParse(configuration["Widgets:VueCompilerPoolSize"], out int parsed)
            ? parsed
            : 2;
        _maxEngines = Math.Clamp(configured, 1, 8);
        _gate = new SemaphoreSlim(_maxEngines, _maxEngines);

        string bundleJs = ReadEmbeddedResource(BundleResourceName);
        string wrapperJs = ReadEmbeddedResource(WrapperResourceName);
        // Prepare (parse) once; each engine executes the pre-parsed AST at warm-up, so the 778 kb parse is not
        // repeated per engine in the pool.
        _preparedBundle = Engine.PrepareScript(bundleJs);
        _preparedWrapper = Engine.PrepareScript(wrapperJs);
    }

    /// <summary>Test seam: how many engines the pool has actually created (proves warmed engines are reused).</summary>
    internal int WarmEngineCount => Volatile.Read(ref _createdEngines);

    public Result<VueSfcOutput> Compile(string source, string filename)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string scopeId = ComputeScopeId(filename, source);
        Engine engine = Rent();
        bool healthy = true;
        try
        {
            JsValue raw = engine.Invoke("__compileSfc", source, filename, scopeId);
            string json = raw.AsString();

            VueSfcCompileResult? compiled = JsonSerializer.Deserialize<VueSfcCompileResult>(
                json,
                JsonOptions
            );
            if (compiled is null)
                return Result.Failure<VueSfcOutput>(
                    "The Vue SFC compiler returned no result.",
                    CompileFailedCode
                );

            if (compiled.Errors is { Count: > 0 })
                return Result.Failure<VueSfcOutput>(
                    FrameErrors(compiled.Errors, filename),
                    CompileFailedCode
                );

            if (string.IsNullOrWhiteSpace(compiled.Code))
                return Result.Failure<VueSfcOutput>(
                    "The Vue SFC produced no module code.",
                    CompileFailedCode
                );

            return Result.Success(new VueSfcOutput(compiled.Code, compiled.Css ?? string.Empty));
        }
        catch (Exception ex)
        {
            // The JS boundary: the wrapper catches compile errors itself, so a throw here is exceptional (a Jint
            // timeout / recursion limit / a corrupt engine). Convert it to a coded failure and drop the engine —
            // a half-executed engine must not go back into the pool.
            healthy = false;
            _logger.LogWarning(ex, "Vue SFC compile threw for {Filename}", filename);
            return Result.Failure<VueSfcOutput>(
                $"The Vue SFC compile could not complete: {ex.Message}",
                CompileFailedCode
            );
        }
        finally
        {
            if (healthy)
                _pool.Enqueue(engine);
            _gate.Release();
        }
    }

    private Engine Rent()
    {
        _gate.Wait();
        return _pool.TryDequeue(out Engine? engine) ? engine : CreateWarmEngine();
    }

    private Engine CreateWarmEngine()
    {
        Engine engine = new(options =>
        {
            options.Strict(false);
            options.TimeoutInterval(TimeSpan.FromSeconds(30));
        });
        engine.SetValue("__sink", new Action<object?>(_ => { }));
        engine.Execute(ConsoleShim);
        engine.Execute(_preparedBundle);
        engine.Execute(PromoteGlobal);
        engine.Execute(_preparedWrapper);
        Interlocked.Increment(ref _createdEngines);
        return engine;
    }

    private static string ReadEmbeddedResource(string name)
    {
        Assembly assembly = typeof(JintVueSfcCompiler).Assembly;
        using System.IO.Stream? stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
            throw new InvalidOperationException(
                $"Embedded resource '{name}' was not found in '{assembly.GetName().Name}'."
            );
        using System.IO.StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // A deterministic scope id (8 hex) per widget source — matches the `data-v-<id>` the scoped CSS is rewritten
    // to and the component's __scopeId, so a given SFC always compiles to the same scoped output.
    private static string ComputeScopeId(string filename, string source)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(filename + "\0" + source));
        return Convert.ToHexStringLower(hash.AsSpan(0, 4));
    }

    private static string FrameErrors(IReadOnlyList<VueCompileError> errors, string filename)
    {
        // @vue/compiler-sfc's thrown parse errors already embed a code frame; structured template/style errors do
        // not, so prefix them with filename:line:column. Join all so nothing is hidden.
        StringBuilder builder = new();
        for (int i = 0; i < errors.Count; i++)
        {
            VueCompileError error = errors[i];
            if (i > 0)
                builder.Append('\n');
            if (error.Line is int line)
                builder
                    .Append(filename)
                    .Append(':')
                    .Append(line)
                    .Append(':')
                    .Append(error.Column ?? 0)
                    .Append(": ");
            builder.Append(error.Message);
        }
        return builder.ToString();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        while (_pool.TryDequeue(out Engine? engine))
            engine.Dispose();
        _gate.Dispose();
    }

    /// <summary>The wrapper's JSON contract: <c>{ code, css, errors:[{ message, line?, column? }] }</c>.</summary>
    private sealed record VueSfcCompileResult(
        string? Code,
        string? Css,
        List<VueCompileError>? Errors
    );

    private sealed record VueCompileError(string Message, int? Line, int? Column);
}
