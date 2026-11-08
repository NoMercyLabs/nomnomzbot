# NomNomzBot dashboard

The Vue dashboard controls a running NomNomzBot backend.
For installation, use [DEPLOY.md](../DEPLOY.md); for API and SDK contracts, start with [Developer documentation](../docs/developer-guide.md).

Use Node.js 22 and run the commands below from `dashboard/`.
The development server listens on port 5090 and proxies bot routes to `http://localhost:5080`.
Start the [backend](../server/README.md#manual-development) separately.
The `NNZ_API` environment variable can select another backend, but saves then affect that backend's real data.
The public marketing build is a separate `npm run build-site` target; see [Site hosting](../docs/site-hosting.md).

## Recommended IDE Setup

[VS Code](https://code.visualstudio.com/) + [Vue (Official)](https://marketplace.visualstudio.com/items?itemName=Vue.volar) (and disable Vetur).

## Recommended Browser Setup

- Chromium-based browsers (Chrome, Edge, Brave, etc.):
  - [Vue.js devtools](https://chromewebstore.google.com/detail/vuejs-devtools/nhdogjmejiglipccpnnnanhbledajbpd)
  - [Turn on Custom Object Formatter in Chrome DevTools](http://bit.ly/object-formatters)
- Firefox:
  - [Vue.js devtools](https://addons.mozilla.org/en-US/firefox/addon/vue-js-devtools/)
  - [Turn on Custom Object Formatter in Firefox DevTools](https://fxdx.dev/firefox-devtools-custom-object-formatters/)

## Type Support for `.vue` Imports in TS

TypeScript cannot handle type information for `.vue` imports by default, so we replace the `tsc` CLI with `vue-tsc` for type checking. In editors, we need [Volar](https://marketplace.visualstudio.com/items?itemName=Vue.volar) to make the TypeScript language service aware of `.vue` types.

## Customize configuration

See [Vite Configuration Reference](https://vite.dev/config/).

## Project Setup

```sh
npm ci
```

### Compile and Hot-Reload for Development

```sh
npm run dev
```

### Type-Check, Compile and Minify for Production

```sh
npm run build
```

### Run Unit Tests with [Vitest](https://vitest.dev/)

```sh
npm run test:unit
```

### Run End-to-End Tests with [Playwright](https://playwright.dev)

The current Playwright configuration still waits for local port 5173, while Vite uses 5090.
The local command below is therefore not a working default until those settings agree.
The CI configuration uses preview on 4173.
Check this configuration before using the end-to-end command; unit tests do not start the backend.

```sh
# Install browsers for the first run
npx playwright install

# When testing on CI, must build the project first
npm run build

# Runs the end-to-end tests
npm run test:e2e
# Runs the tests only on Chromium
npm run test:e2e -- --project=chromium
# Runs the tests of a specific file
npm run test:e2e -- tests/example.spec.ts
# Runs the tests in debug mode
npm run test:e2e -- --debug
```

### Lint with [ESLint](https://eslint.org/)

```sh
npm run lint   # checks only, changes nothing
npm run fix    # applies the lint fixes to every file
```
