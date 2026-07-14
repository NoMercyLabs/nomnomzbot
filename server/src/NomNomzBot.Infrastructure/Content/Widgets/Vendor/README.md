# Vendored Vue SFC compiler + runtime

These files back the Vue widget path. The first two power `JintVueSfcCompiler` (server-side Vue
Single-File-Component compilation inside Jint, no Node runtime) — both embedded resources (see
`NomNomzBot.Infrastructure.csproj`) loaded once into each pooled Jint engine at warm-up. The third is the
matching Vue **runtime**, served to the browser (not loaded into Jint).

## `vue-compiler-sfc.js` (vendored, third-party)

A self-contained IIFE bundle of **`@vue/compiler-sfc@3.5.39`** (MIT — © Vue.js contributors), exposing the
whole compiler surface on the global `VueCompilerSFC`. It is filesystem-free and runs inside Jint with only a
5-line `console` shim; no other polyfills.

Regenerate (from a scratch dir with Node + npm) whenever the pinned Vue version changes:

```bash
npm init -y
npm i @vue/compiler-sfc@3.5.39 esbuild@0.28.1
printf "export * from '@vue/compiler-sfc'\n" > entry.js
npx esbuild entry.js \
  --bundle --format=iife --global-name=VueCompilerSFC \
  --platform=browser --target=es2020 --minify \
  --outfile=vue-compiler-sfc.js
```

Produces ~778 kb. Do NOT hand-edit the bundle; only regenerate.

## `compile-sfc.js` (ours, AGPL)

A thin wrapper (loaded after the bundle) that assembles an SFC into ONE ES module the way `@vue/repl` does —
`compileScript` (→ `rewriteDefault` to the stable binding `__sfc_main__`) + `compileTemplate` (render bound
onto the component) + `compileStyle` (scoped). It exposes `globalThis.__compileSfc(source, filename, id)`
returning a JSON string `{ code, css, errors:[{message,line?,column?}] }`.

Notes:

- The emitted `code` keeps its `vue` imports **bare/external** and still contains TS/JSX syntax
  (`compileScript` does not strip types) — the caller's esbuild stage strips it with the `ts` loader and maps
  the external `vue` to the host `window.Vue`.
- `rewriteDefault` is called with the block's babel parser plugins (`typescript`/`jsx`), or it throws on a
  generic (e.g. `ref<number>`, `setup(__props: any)`) — a subtle but load-bearing detail.

## `vue.runtime.global.prod.js` (vendored, third-party)

The **`vue@3.5.39`** runtime global build (MIT — © Vue.js contributors), ~104 kb, exposing `window.Vue`
(`createApp` + the compiled-render-fn helpers `_toDisplayString`, `openBlock`, `createElementBlock`, …). Its
version **MUST** match `@vue/compiler-sfc` above — the compiler emits render code the runtime executes.

`OverlayVueRuntimeController` serves this at `/overlay/vue.js`; the overlay host injects that script into a
vue widget's sandboxed iframe **before** the widget bundle, so `window.Vue` is defined when the bundle
self-mounts. It is not loaded into Jint.

Regenerate whenever the pinned Vue version changes (from a scratch dir with Node + npm):

```bash
npm i vue@3.5.39
cp node_modules/vue/dist/vue.runtime.global.prod.js .
```

Do NOT hand-edit; only re-copy the dist file.
