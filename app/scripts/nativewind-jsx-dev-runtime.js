'use strict'
/**
 * Stub for nativewind/jsx-dev-runtime (web only).
 *
 * nativewind@5.0.0-preview.3 does not export jsx-runtime / jsx-dev-runtime,
 * but babel-preset-expo with jsxImportSource:'nativewind' emits
 *   import { jsxDEV } from 'nativewind/jsx-dev-runtime'
 * This stub satisfies that import AND implements the className forwarding
 * using react-native-web's $$css trick:
 *   style: { $$css: true, className: 'flex-1 bg-gray-900 ...' }
 * RNW detects $$css:true and writes the value as a real DOM class attribute,
 * so Tailwind utilities in global.css take effect.
 */

var React = require('react')
var runtime = require('react/jsx-dev-runtime')
var Fragment = React.Fragment

function forwardClassName(props) {
  if (!props || typeof props.className !== 'string' || !props.className) {
    return props
  }
  var className = props.className
  var newProps = Object.assign({}, props)
  delete newProps.className
  // RNW $$css: style entries with $$css:true are applied as DOM class strings.
  newProps.style = [props.style, { $$css: true, className: className }]
  return newProps
}

function createElement(type, props) {
  var children = Array.prototype.slice.call(arguments, 2)
  var nextProps = typeof type === 'string' ? props : forwardClassName(props)
  return React.createElement.apply(React, [type, nextProps].concat(children))
}

function jsxDEV(type, props, key, isStaticChildren, source, self) {
  // For plain HTML elements className is a native DOM attribute — don't transform.
  if (typeof type === 'string') {
    return runtime.jsxDEV(type, props, key, isStaticChildren, source, self)
  }
  return runtime.jsxDEV(type, forwardClassName(props), key, isStaticChildren, source, self)
}

module.exports = { jsxDEV: jsxDEV, createElement: createElement, Fragment: Fragment }
