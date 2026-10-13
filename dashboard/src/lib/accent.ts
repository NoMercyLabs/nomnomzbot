// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The accent family comes from one channel colour, as in the approved mockups (base/accent.js,
// itself a port of Tokens.withAccent()). Blends happen in gamma sRGB, as Compose Color does.

type Rgb = [number, number, number]

export type Theme = 'dark' | 'light'

// The colour the mockups were approved with; it stands in until a channel is chosen.
export const defaultAccent: string = '#FF014F'

const schemes = {
  dark: {
    background: '#111111',
    foreground: '#eeeeee',
    muted: '#222222',
    sidebar: '#0f0f0f',
    hint: '#b0b0b0',
    link: '#60a5fa',
  },
  light: {
    background: '#ffffff',
    foreground: '#0a0a0a',
    muted: '#f5f5f5',
    sidebar: '#f2f2f2',
    hint: '#636363',
    link: '#1d4ed8',
  },
} as const satisfies Record<
  Theme,
  Record<'background' | 'foreground' | 'muted' | 'sidebar' | 'hint' | 'link', string>
>
const nearWhite: Rgb = [250 / 255, 250 / 255, 250 / 255]
const nearBlack: Rgb = [23 / 255, 23 / 255, 23 / 255]

function parse(hex: string): Rgb | null {
  const digits = hex.trim().replace(/^#/, '')

  if (!/^[0-9a-fA-F]{6}$/.test(digits)) return null

  return [0, 2, 4].map((i) => parseInt(digits.slice(i, i + 2), 16) / 255) as Rgb
}

function rgb(hex: string): Rgb {
  return parse(hex)!
}

function css(colour: Rgb): string {
  return `rgb(${colour.map((v) => Math.round(Math.min(1, Math.max(0, v)) * 255)).join(' ')})`
}

function luminance(colour: Rgb): number {
  const linear = (v: number): number =>
    v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4)

  return 0.2126 * linear(colour[0]) + 0.7152 * linear(colour[1]) + 0.0722 * linear(colour[2])
}

function contrast(a: Rgb, b: Rgb): number {
  const x = luminance(a)
  const y = luminance(b)

  return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05)
}

function blend(a: Rgb, b: Rgb, f: number): Rgb {
  return a.map((v, i) => v + (b[i]! - v) * f) as Rgb
}

// Text on the colour: whichever of near white and near black reads better.
function ink(colour: Rgb): Rgb {
  return contrast(colour, nearWhite) >= contrast(colour, nearBlack) ? nearWhite : nearBlack
}

// Moves the colour toward `toward` until it reads at `min` on the background and with its own ink.
function readableOn(colour: Rgb, background: Rgb, toward: Rgb, min: number): Rgb {
  let c: Rgb = colour

  for (let i = 0; i < 20; i++) {
    if (contrast(c, background) >= min && contrast(c, ink(c)) >= min) return c
    c = blend(c, toward, 0.08)
  }

  return c
}

function readableWith(colour: Rgb, other: Rgb, toward: Rgb, min: number): Rgb {
  let c: Rgb = colour

  for (let i = 0; i < 30; i++) {
    if (contrast(c, other) >= min) return c
    c = blend(c, toward, 0.08)
  }

  return c
}

// A selected row carries hint text and links; the tint gives way until both still read at 4.5:1.
function tint(base: Rgb, accent: Rgb, start: number, theme: Theme): Rgb {
  let f: number = start
  let c: Rgb = blend(base, accent, f)

  while (
    f > 0.02 &&
    (contrast(c, rgb(schemes[theme].hint)) < 4.5 || contrast(c, rgb(schemes[theme].link)) < 4.5)
  ) {
    f -= 0.02
    c = blend(base, accent, f)
  }

  return c
}

export function accentTokens(hex: string, theme: Theme): Record<string, string> {
  const raw = parse(hex)

  if (!raw) return {}
  const scheme = schemes[theme]
  const background = rgb(scheme.background)
  const foreground = rgb(scheme.foreground)
  const accent = readableOn(raw, background, foreground, 4.5)

  return {
    primary: css(accent),
    'primary-foreground': css(ink(accent)),
    secondary: css(blend(background, accent, 0.32)),
    'secondary-foreground': css(foreground),
    accent: css(tint(rgb(scheme.muted), accent, 0.2, theme)),
    'accent-foreground': css(foreground),
    ring: css(accent),
    'sidebar-primary': css(readableWith(raw, nearWhite, nearBlack, 4.5)),
    'sidebar-primary-foreground': css(nearWhite),
    'sidebar-accent': css(tint(rgb(scheme.sidebar), accent, 0.18, theme)),
    'sidebar-accent-foreground': css(foreground),
    'sidebar-ring': css(accent),
  }
}

export function applyAccent(hex: string): void {
  const root = document.documentElement
  const theme: Theme = root.classList.contains('light') ? 'light' : 'dark'

  for (const [name, value] of Object.entries(accentTokens(hex, theme)))
    root.style.setProperty(`--${name}`, value)
}
