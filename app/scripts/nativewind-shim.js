'use strict'
/**
 * Web shim for the `nativewind` package.
 *
 * With `jsxImportSource: 'nativewind'`, some modules (notably `expo-router`)
 * end up calling `nativewind.createElement(...)`. preview.3 does not export that
 * helper, so we provide it here while re-exporting the real package API.
 */

var React = require('react')
var realNativewind = require('../node_modules/nativewind/dist/commonjs/index.js')

function forwardClassName(props) {
  if (!props || typeof props.className !== 'string' || !props.className) {
    return props
  }

  var className = props.className
  var nextProps = Object.assign({}, props)
  delete nextProps.className
  nextProps.style = [props.style, { $$css: true, className: className }]
  return nextProps
}

function createElement(type, props) {
  var children = Array.prototype.slice.call(arguments, 2)
  var nextProps = typeof type === 'string' ? props : forwardClassName(props)
  return React.createElement.apply(React, [type, nextProps].concat(children))
}

var shim = Object.assign({}, realNativewind, {
  createElement: createElement,
  Fragment: React.Fragment,
})

shim.default = shim

module.exports = shim
