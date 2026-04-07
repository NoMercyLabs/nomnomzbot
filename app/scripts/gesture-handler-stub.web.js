'use strict';
/**
 * Web stub for react-native-gesture-handler.
 *
 * On web, gesture-handler pulls in react-native-reanimated which initialises
 * ReanimatedFlatList at startup, triggering a broken FlatList getter in
 * react-native-web 0.21.x and crashing the app before anything renders.
 *
 * We only use GestureHandlerRootView from this package — on web it's a no-op
 * wrapper, so a plain View is a perfect substitute.
 */
const React = require('react');
const { View } = require('react-native');

function GestureHandlerRootView({ style, children }) {
  return React.createElement(View, { style }, children);
}

module.exports = {
  GestureHandlerRootView,
  // Commonly used gesture classes — safe no-ops on web
  GestureDetector: ({ children }) => children,
  Gesture: { Tap: () => ({}), Pan: () => ({}), Pinch: () => ({}), Rotation: () => ({}) },
  State: {},
  Directions: {},
};
