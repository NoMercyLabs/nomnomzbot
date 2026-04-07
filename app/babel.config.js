module.exports = function (api) {
  // On web, nativewind/babel (react-native-css's import-plugin) rewrites every
  // `import { View } from 'react-native'` to `import { View } from 'react-native-css/components/View'`.
  // Each wrapper does a top-level `require('react-native')` — while react-native-web
  // is itself loading, this creates a circular dep that Metro converts to a lazy
  // getter which fires on undefined → crash.
  //
  // On web, NativeWind 5 is CSS-first: className becomes a real HTML class
  // attribute backed by global.css. The runtime polyfill is not needed and
  // actively harmful on web. Skip the preset for web builds.
  const isWeb = api.caller((caller) => caller?.platform === 'web')

  // Cache separately per platform so native builds still get the full preset.
  api.cache.invalidate(() => isWeb)

  const presets = isWeb
    ? ['babel-preset-expo']
    : ['babel-preset-expo', 'nativewind/babel']

  return {
    presets,
    plugins: ['react-native-reanimated/plugin'],
  }
}
