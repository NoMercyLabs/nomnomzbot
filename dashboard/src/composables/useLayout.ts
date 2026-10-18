// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { type MaybeComputedElementRef, useBreakpoints, useElementSize, useMediaQuery } from '@vueuse/core';
import { computed, type ComputedRef, type Ref } from 'vue';

// The one place the dashboard asks "is there room?". A view never writes a width into a media query or a size
// check of its own (structure.spec guards it): it names a width from this table and gets a ref back.
// Stoney 2026-10-18: "extract things like these into reusables".
export const widths = {
  // The frame shows an icon rail from here; below it, a bottom bar.
  rail: 600,
  // Below this width a screen is a phone width (a phone also has a coarse pointer, see usePhone).
  tablet: 768,
  // The frame shows the full sidebar from here.
  sidebar: 840,
  // A side panel folds in beside the 200px-narrower rail from here.
  besideRail: 1080,
  // The board holds three readable panels from this width; below it, one panel at a time behind tabs.
  board: 1100,
  // A side panel sits beside the content from here.
  wide: 1280,
  // The music rules sit as a third column from this board width.
  rulesInline: 1440,
  // Chat keeps two side panels open together from this window width.
  twoPanels: 1800,
} as const satisfies Record<string, number>;

export type LayoutWidth = keyof typeof widths;

// The window is at least this wide.
export function useScreenFrom(name: LayoutWidth): ComputedRef<boolean> {
  return useBreakpoints(widths).greaterOrEqual(name);
}

// The window is narrower than a tablet, a touch screen or not.
export function useNarrowScreen(): ComputedRef<boolean> {
  return useBreakpoints(widths).smaller('tablet');
}

// A phone: a narrow window with a coarse (touch) pointer. A narrow desktop window is not a phone.
export function usePhone(): ComputedRef<boolean> {
  const narrow = useNarrowScreen();
  const touch = useMediaQuery('(pointer: coarse)');

  return computed(() =>
    narrow.value && touch.value);
}

// Whether an element (not the window) is at least the named width, for a box that shares the window with a
// sidebar or a drawer. Before the first measure it assumes the window width.
export function useFits(
  target: MaybeComputedElementRef,
  name: LayoutWidth,
): { width: Ref<number>; fits: ComputedRef<boolean> } {
  const { width } = useElementSize(target, { width: window.innerWidth, height: 0 });
  const fits = computed(() =>
    width.value >= widths[name]);

  return { width, fits };
}
