const path = require('path')
const { getDefaultConfig } = require('expo/metro-config')
const { withNativewind } = require('nativewind/metro')

const FLATLIST_STUB = path.resolve(__dirname, 'scripts/FlatListStub.web.js')
const GESTURE_HANDLER_STUB = path.resolve(__dirname, 'scripts/gesture-handler-stub.web.js')
const REANIMATED_STUB = path.resolve(__dirname, 'scripts/reanimated-web-stub.js')

const config = getDefaultConfig(__dirname)

// ---------------------------------------------------------------------------
// Fix 1: zustand ESM → CJS
//
// Metro sets isESMImport=true for files with `import` statements, picking the
// "import" condition from package exports maps. zustand/middleware resolves to
// esm/middleware.mjs which contains `import.meta.env` — a syntax error in a
// non-module <script>. Force CJS (the "require"/"default" condition) instead.
//
// Fix 2: pretty-format default export
//
// @expo/metro-runtime's HMRClient does `import prettyFormat from 'pretty-format'`
// which Metro's ESM interop compiles to access `.default`. pretty-format v30's
// CJS build sets `exports.default = void 0`, so the HMR client crashes.
// Redirect to a shim that exposes default = format.
// ---------------------------------------------------------------------------
const _defaultResolveRequest = config.resolver.resolveRequest

const ESM_TO_CJS_PACKAGES = ['zustand']
const PRETTY_FORMAT_SHIM = path.resolve(__dirname, 'scripts/pretty-format-shim.js')

config.resolver.resolveRequest = (context, moduleName, platform) => {
  // pretty-format → shim with correct default export
  if (moduleName === 'pretty-format') {
    return { type: 'sourceFile', filePath: PRETTY_FORMAT_SHIM }
  }

  // react-native-css (NativeWind's webResolver) intercepts react-native-web's
  // FlatList/SectionList and redirects to its own wrappers. Those wrappers access
  // require('react-native').FlatList at module init time, which hits a partially-
  // initialized react-native-web CJS index and crashes. Block them on web.
  if (platform === 'web' && (
    moduleName === 'react-native-css/components/FlatList' ||
    moduleName === 'react-native-css/components/SectionList' ||
    moduleName === 'react-native-css/components/VirtualizedList'
  )) {
    return { type: 'sourceFile', filePath: FLATLIST_STUB }
  }

  // react-native-css (used by NativeWind's withNativewind) has a webResolver that
  // intercepts react-native-web/dist/cjs/exports/FlatList/index.js and redirects
  // it to react-native-css/components/FlatList.js. That component does
  // require('react-native').FlatList — which hits the partially-initialized
  // react-native-web CJS index (circular require), where FlatList is still void 0
  // (set as placeholder at the top of the file). The undefined propagates into
  // Metro's lazy ESM getter → crash: "Cannot read properties of undefined (reading 'default')".
  //
  // Fix: intercept react-native-web's own internal FlatList sub-module here
  // (as the parentResolver that NativeWind calls). Our stub is returned instead;
  // NativeWind's webResolver sees the path is NOT inside react-native-web and
  // leaves it alone — circular dependency broken.
  if (platform === 'web') {
    const origin = context.originModulePath?.replace(/\\/g, '/') ?? ''
    if (
      origin.includes('/react-native-web/') && (
        moduleName === './exports/FlatList' ||
        moduleName.endsWith('/exports/FlatList') ||
        moduleName.includes('/vendor/react-native/FlatList')
      )
    ) {
      return { type: 'sourceFile', filePath: FLATLIST_STUB }
    }
  }

  // react-native-reanimated@4.x accesses { FlatList } from 'react-native' at
  // module init (ReanimatedFlatList wrapper). react-native-web@0.21.x no
  // longer ships FlatList — its getter returns `_FlatList.default` where
  // `_FlatList` is undefined, crashing the bundle before anything renders.
  // On web, redirect every reanimated import to a safe no-op stub.
  if (platform === 'web' && (
    moduleName === 'react-native-reanimated' ||
    moduleName.startsWith('react-native-reanimated/')
  )) {
    return { type: 'sourceFile', filePath: REANIMATED_STUB }
  }

  // react-native-gesture-handler on web pulls in reanimated which triggers the broken
  // FlatList getter at startup. Redirect to a minimal stub that only wraps View.
  if (platform === 'web' && (
    moduleName === 'react-native-gesture-handler' ||
    moduleName.startsWith('react-native-gesture-handler/')
  )) {
    return { type: 'sourceFile', filePath: GESTURE_HANDLER_STUB }
  }

  // react-native-web 0.21.x has a broken FlatList getter (return D.default where D=undefined).
  // react-native-reanimated@4.x triggers it at startup via itemLayoutAnimation → ReanimatedFlatList.
  // On web, redirect every FlatList import to a safe ScrollView-based stub.
  if (platform === 'web' && (
    moduleName === 'react-native/Libraries/Lists/FlatList' ||
    moduleName === 'react-native/Libraries/Lists/VirtualizedList' ||
    moduleName === 'react-native/Libraries/Lists/VirtualizedList_EXPERIMENTAL'
  )) {
    return { type: 'sourceFile', filePath: FLATLIST_STUB }
  }

  // zustand/* → force CJS build (no import.meta.env)
  const needsCjsRedirect = ESM_TO_CJS_PACKAGES.some(
    (pkg) => moduleName === pkg || moduleName.startsWith(pkg + '/'),
  )
  if (needsCjsRedirect && context.isESMImport) {
    return (_defaultResolveRequest ?? context.resolveRequest)(
      { ...context, isESMImport: false },
      moduleName,
      platform,
    )
  }

  if (_defaultResolveRequest) {
    return _defaultResolveRequest(context, moduleName, platform)
  }
  return context.resolveRequest(context, moduleName, platform)
}

module.exports = withNativewind(config, {
  input: './global.css',
  inlineVariables: false,
  // Disable react-native-css's webResolver. That resolver intercepts every
  // react-native-web component sub-path (View, Text, FlatList, Pressable…) and
  // redirects them to react-native-css/components/*.js wrappers. Each wrapper does
  // a top-level require('react-native') at module init time. While react-native-web
  // itself is still initialising this creates a circular dep → Metro wraps the
  // unfinished export in a lazy getter → getter fires before the module finishes →
  // "Cannot read properties of undefined (reading 'default')".
  //
  // NativeWind 5 is CSS-first: Tailwind classes live in global.css and are applied
  // as real HTML class attributes on web. The runtime className→inline-style
  // polyfill (globalClassNamePolyfill) is the old NativeWind 4 approach and is not
  // needed — and actively harmful — for the CSS-first build.
  globalClassNamePolyfill: false,
})
