'use strict'
/**
 * Stub for nativewind/jsx-runtime (web only).
 * Same $$css trick as nativewind-jsx-dev-runtime.js but for production builds.
 */

var React = require('react')
var runtime = require('react/jsx-runtime')
var Fragment = React.Fragment

function forwardClassName(props) {
  if (!props || typeof props.className !== 'string' || !props.className) {
    return props
  }
  var className = props.className
  var newProps = Object.assign({}, props)
  delete newProps.className
  newProps.style = [props.style, { $$css: true, className: className }]
  return newProps
}

function jsx(type, props, key) {
  if (typeof type === 'string') return runtime.jsx(type, props, key)
  return runtime.jsx(type, forwardClassName(props), key)
}

function jsxs(type, props, key) {
  if (typeof type === 'string') return runtime.jsxs(type, props, key)
  return runtime.jsxs(type, forwardClassName(props), key)
}

function createElement(type, props) {
  var children = Array.prototype.slice.call(arguments, 2)
  var nextProps = typeof type === 'string' ? props : forwardClassName(props)
  return React.createElement.apply(React, [type, nextProps].concat(children))
}

module.exports = { jsx: jsx, jsxs: jsxs, createElement: createElement, Fragment: Fragment }
