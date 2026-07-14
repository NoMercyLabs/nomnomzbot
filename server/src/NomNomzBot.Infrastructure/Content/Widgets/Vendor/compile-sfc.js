// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// Assembles a single-file SFC into ONE ES module, mirroring how @vue/repl composes an SFC:
// compileScript (rewriteDefault -> const __sfc_main__) + compileTemplate (render bound onto the
// component) + compileStyle (scoped). Depends on globalThis.VueCompilerSFC (the esbuild IIFE of
// @vue/compiler-sfc, loaded first). Exposes:
//   globalThis.__compileSfc(source, filename, id) -> JSON string { code, css, errors:[{message,line?,column?}] }
// The emitted `code` default-exports the component under the STABLE binding name `__sfc_main__` and keeps
// its `vue` imports bare/external — the caller's esbuild stage maps `vue` to the host Vue global and appends
// the mount. `code` still contains TS/JSX syntax (compileScript does not strip types); the caller strips it
// with esbuild's `ts` loader.
(function () {
  var C = globalThis.VueCompilerSFC;
  if (!C) throw new Error('VueCompilerSFC global missing');

  function toError(e) {
    var out = { message: (e && e.message) ? String(e.message) : String(e) };
    var loc = e && e.loc ? (e.loc.start || e.loc) : null;
    if (loc && typeof loc.line === 'number') out.line = loc.line;
    if (loc && typeof loc.column === 'number') out.column = loc.column;
    return out;
  }

  globalThis.__compileSfc = function (source, filename, id) {
    var errors = [];
    var result = { code: '', css: '', errors: errors };
    try {
      var parsed = C.parse(source, { filename: filename });
      if (parsed.errors && parsed.errors.length) {
        for (var i = 0; i < parsed.errors.length; i++) errors.push(toError(parsed.errors[i]));
      }
      var descriptor = parsed.descriptor;

      var hasScoped = false;
      if (descriptor.styles) {
        for (var s = 0; s < descriptor.styles.length; s++) if (descriptor.styles[s].scoped) hasScoped = true;
      }

      // --- <script> / <script setup> --> a named binding so the render fn + metadata can attach ---
      var bindings = null;
      var scriptCode = 'const __sfc_main__ = {};';
      if (descriptor.script || descriptor.scriptSetup) {
        var compiled = C.compileScript(descriptor, { id: id, inlineTemplate: false });
        bindings = compiled.bindings || null;
        // compileScript keeps TS/JSX syntax (the bundler strips it downstream), so rewriteDefault must
        // re-parse the compiled content with the SAME babel plugins the block's `lang` implies, or it
        // chokes on a generic (e.g. `ref<number>` / `setup(__props: any)`).
        var lang = (descriptor.scriptSetup && descriptor.scriptSetup.lang) ||
          (descriptor.script && descriptor.script.lang) || '';
        var plugins = [];
        if (lang === 'ts' || lang === 'tsx') plugins.push('typescript');
        if (lang === 'jsx' || lang === 'tsx') plugins.push('jsx');
        scriptCode = C.rewriteDefault(compiled.content, '__sfc_main__', plugins);
      }

      // --- <template> --> a standalone render fn (its bare `vue` import is kept for the bundler) ---
      var templateCode = '';
      if (descriptor.template) {
        var tpl = C.compileTemplate({
          source: descriptor.template.content,
          filename: filename,
          id: id,
          scoped: hasScoped,
          slotted: descriptor.slotted,
          compilerOptions: { bindingMetadata: bindings }
        });
        if (tpl.errors && tpl.errors.length) {
          for (var t = 0; t < tpl.errors.length; t++) errors.push(toError(tpl.errors[t]));
        }
        templateCode = tpl.code.replace(/\bexport function (render|ssrRender)\b/, 'function $1');
      }

      // --- <style scoped> --> collected CSS (returned separately for host <style> injection) ---
      var css = '';
      if (descriptor.styles) {
        for (var st = 0; st < descriptor.styles.length; st++) {
          var styled = C.compileStyle({
            source: descriptor.styles[st].content,
            filename: filename,
            id: 'data-v-' + id,
            scoped: descriptor.styles[st].scoped
          });
          if (styled.errors && styled.errors.length) {
            for (var e2 = 0; e2 < styled.errors.length; e2++) errors.push(toError(styled.errors[e2]));
          }
          css += styled.code + '\n';
        }
      }

      // --- assemble one ES module: component object + render + scope id, default-exported ---
      var parts = [scriptCode];
      if (templateCode) {
        parts.push(templateCode);
        parts.push('__sfc_main__.render = render;');
      }
      if (hasScoped) parts.push('__sfc_main__.__scopeId = "data-v-' + id + '";');
      parts.push('__sfc_main__.__file = ' + JSON.stringify(filename) + ';');
      parts.push('export default __sfc_main__;');

      result.code = parts.join('\n');
      result.css = css;
    } catch (e) {
      errors.push(toError(e));
    }
    return JSON.stringify(result);
  };
})();
