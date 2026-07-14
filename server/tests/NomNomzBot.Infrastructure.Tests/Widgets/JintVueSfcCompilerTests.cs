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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Widgets.Bundling;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves the Jint-hosted Vue SFC compiler produces a real, shaped ES module + scoped CSS from a
/// <c>&lt;script setup lang="ts"&gt;</c> SFC, reports broken SFCs as a coded failure with a framed message, and
/// reuses its warmed engine across compiles instead of rebuilding one each time.
/// </summary>
public sealed class JintVueSfcCompilerTests : IClassFixture<VueSfcCompilerFixture>
{
    private readonly VueSfcCompilerFixture _fixture;

    public JintVueSfcCompilerTests(VueSfcCompilerFixture fixture) => _fixture = fixture;

    private const string RepresentativeSfc = """
        <script setup lang="ts">
        import { ref, computed } from 'vue'
        const props = defineProps<{ label: string }>()
        const count = ref<number>(0)
        const doubled = computed<number>(() => count.value * 2)
        function increment(): void { count.value++ }
        </script>
        <template>
          <button class="counter" @click="increment">{{ props.label }}: {{ count }} (x2={{ doubled }})</button>
        </template>
        <style scoped>
        .counter { color: v-bind(count); background: var(--bg); padding: 8px; }
        </style>
        """;

    private const string BrokenSfc = """
        <script setup lang="ts">
        import { ref } from 'vue'
        const n = ref<number>(
        </script>
        <template><p>{{ n }}</p></template>
        """;

    [Fact]
    public void Representative_ts_sfc_compiles_to_a_vue_module_with_render_and_scoped_style()
    {
        Result<VueSfcOutput> result = _fixture.Compiler.Compile(RepresentativeSfc, "Counter.vue");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        VueSfcOutput output = result.Value;

        // A real ES module that imports the (external) Vue runtime and default-exports the component binding.
        output.ModuleCode.Should().MatchRegex("from ['\"]vue['\"]");
        output.ModuleCode.Should().Contain("export default __sfc_main__");
        // The <template> became a render fn carrying the interpolated bindings, bound onto the component.
        output.ModuleCode.Should().Contain("function render");
        output.ModuleCode.Should().Contain("_toDisplayString");
        output.ModuleCode.Should().Contain("__sfc_main__.render = render");
        // Scoped: the component carries a deterministic data-v scope id, and the scoped CSS is rewritten to the
        // matching attribute selector, with the v-bind() reactive style compiled to a CSS custom property.
        output.ModuleCode.Should().MatchRegex("__scopeId = \"data-v-[0-9a-f]{8}\"");
        output.Css.Should().MatchRegex(@"\.counter\[data-v-[0-9a-f]{8}\]");
        output.Css.Should().Contain("var(--");
    }

    [Fact]
    public void Plain_js_sfc_without_types_also_compiles()
    {
        const string plain = """
            <script setup>
            import { ref } from 'vue'
            const message = ref('hi')
            </script>
            <template><p>{{ message }}</p></template>
            """;

        Result<VueSfcOutput> result = _fixture.Compiler.Compile(plain, "Plain.vue");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.ModuleCode.Should().Contain("_toDisplayString").And.Contain("message");
        result.Value.Css.Should().BeEmpty(); // no <style> block
    }

    [Fact]
    public void Compilation_is_deterministic_for_a_given_source()
    {
        Result<VueSfcOutput> first = _fixture.Compiler.Compile(RepresentativeSfc, "Same.vue");
        Result<VueSfcOutput> second = _fixture.Compiler.Compile(RepresentativeSfc, "Same.vue");

        first.IsSuccess.Should().BeTrue(first.ErrorMessage);
        second.IsSuccess.Should().BeTrue(second.ErrorMessage);
        // Stable output (same scope id, same bytes) is what makes the downstream content hash a valid cache key.
        second.Value.ModuleCode.Should().Be(first.Value.ModuleCode);
        second.Value.Css.Should().Be(first.Value.Css);
    }

    [Fact]
    public void Broken_sfc_fails_with_the_compile_failed_code_and_a_framed_message()
    {
        Result<VueSfcOutput> result = _fixture.Compiler.Compile(BrokenSfc, "Broken.vue");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(JintVueSfcCompiler.CompileFailedCode);
        result.ErrorCode.Should().Be("WIDGET_VUE_COMPILE_FAILED");
        // The message is the compiler's code frame — it names the file and the offending location, not an empty
        // or generic string.
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        result.ErrorMessage.Should().Contain("Broken.vue");
    }

    [Fact]
    public void The_warmed_engine_is_reused_across_compiles()
    {
        // A fresh instance so the created-engine count is deterministic (the shared fixture accumulates across the
        // class). Two sequential compiles must be served by ONE warmed engine, never a fresh engine per compile.
        using JintVueSfcCompiler compiler = new(
            new ConfigurationBuilder().Build(),
            NullLogger<JintVueSfcCompiler>.Instance
        );

        Result<VueSfcOutput> first = compiler.Compile(RepresentativeSfc, "A.vue");
        Result<VueSfcOutput> second = compiler.Compile(RepresentativeSfc, "B.vue");

        first.IsSuccess.Should().BeTrue(first.ErrorMessage);
        second.IsSuccess.Should().BeTrue(second.ErrorMessage);
        compiler.WarmEngineCount.Should().Be(1);
    }
}
