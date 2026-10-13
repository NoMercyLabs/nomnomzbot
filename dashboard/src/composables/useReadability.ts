// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useStorage } from '@vueuse/core'
import { computed, watch, type ComputedRef, type Ref } from 'vue'

// How text looks for one person on one device. No server endpoint exists for it, so the options live in
// this browser (localStorage) and reach the page as attributes and custom properties on <html>.
export type ReadabilityFont = 'system' | 'hyperlegible' | 'lexend'
export type ReadabilityLineSpacing = 'normal' | 'wide' | 'wider'
export type ReadabilitySpacing = 'normal' | 'wide'
export type ReadabilityMotion = 'system' | 'reduce'
export type ReadabilityDensity = 'compact' | 'comfortable' | 'touch'
export type ReadabilityPreset = 'default' | 'dyslexia' | 'contrast'

export interface Readability {
  font: ReadabilityFont
  size: number
  lh: ReadabilityLineSpacing
  spacing: ReadabilitySpacing
  contrast: boolean
  motion: ReadabilityMotion
  density: ReadabilityDensity
}

export const readabilityStorageKey = 'nzb:readability'
export const readabilitySizeMin = 12
export const readabilitySizeMax = 20

export const readabilityDefaults = {
  font: 'system',
  size: 14,
  lh: 'normal',
  spacing: 'normal',
  contrast: false,
  motion: 'system',
  density: 'compact',
} as const satisfies Readability

export const readabilityPresets = {
  default: readabilityDefaults,
  dyslexia: {
    font: 'lexend',
    size: 16,
    lh: 'wide',
    spacing: 'wide',
    contrast: false,
    motion: 'reduce',
    density: 'comfortable',
  },
  contrast: {
    font: 'system',
    size: 16,
    lh: 'normal',
    spacing: 'normal',
    contrast: true,
    motion: 'system',
    density: 'comfortable',
  },
} as const satisfies Record<ReadabilityPreset, Readability>

// The design tokens are px values at the 14 px body size; a text size scales all of them by the same factor.
const baseSizePx = 14
const textTokens = {
  xs: { size: 12, lh: 16 },
  sm: { size: 14, lh: 20 },
  base: { size: 16, lh: 24 },
  lg: { size: 18, lh: 28 },
  xl: { size: 20, lh: 28 },
  '2xl': { size: 24, lh: 32 },
} as const satisfies Record<string, { size: number; lh: number }>
const lineFactor = { normal: 1, wide: 1.15, wider: 1.3 } as const satisfies Record<
  ReadabilityLineSpacing,
  number
>

const px = (value: number): string => `${Math.round(value * 100) / 100}px`

function setFlag(root: HTMLElement, name: string, on: boolean, value: string): void {
  if (on) {
    root.setAttribute(name, value)
  }
  else {
    root.removeAttribute(name)
  }
}

export function applyReadability(
  settings: Readability,
  root: HTMLElement = document.documentElement,
): void {
  const scale = settings.size / baseSizePx

  for (const [name, token] of Object.entries(textTokens)) {
    root.style.setProperty(`--text-${name}`, px(token.size * scale))
    root.style.setProperty(`--lh-${name}`, px(token.lh * scale * lineFactor[settings.lh]))
  }

  setFlag(root, 'data-rd-font', settings.font !== 'system', settings.font)
  setFlag(root, 'data-rd-lh', settings.lh !== 'normal', settings.lh)
  setFlag(root, 'data-rd-spacing', settings.spacing !== 'normal', settings.spacing)
  setFlag(root, 'data-rd-contrast', settings.contrast, 'on')
  setFlag(root, 'data-rd-motion', settings.motion !== 'system', settings.motion)
  root.setAttribute('data-density', settings.density)
}

export interface UseReadability {
  settings: Ref<Readability>
  activePreset: ComputedRef<ReadabilityPreset | null>
  applyPreset: (preset: ReadabilityPreset) => void
}

export function useReadability(): UseReadability {
  const settings: Ref<Readability> = useStorage<Readability>(
    readabilityStorageKey,
    { ...readabilityDefaults },
    undefined,
    {
      mergeDefaults: true,
    },
  )

  watch(settings, (next: Readability): void => applyReadability(next), {
    deep: true,
    immediate: true,
    flush: 'sync',
  })

  const activePreset: ComputedRef<ReadabilityPreset | null> = computed<ReadabilityPreset | null>(
    () => {
      const names = Object.keys(readabilityPresets) as ReadabilityPreset[]
      const match = names.find((name: ReadabilityPreset) =>
        (Object.keys(readabilityDefaults) as (keyof Readability)[]).every(
          (key: keyof Readability) => settings.value[key] === readabilityPresets[name][key],
        ),
      )

      return match ?? null
    },
  )

  function applyPreset(preset: ReadabilityPreset): void {
    settings.value = { ...readabilityPresets[preset] }
  }

  return { settings, activePreset, applyPreset }
}
