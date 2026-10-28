// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

// jsdom has no matchMedia, so a page under test would always lay out as a phone. setWidth gives the window
// a width: matchMedia answers min-width queries against it.
export function setWidth(width: number): void {
  // A board measures itself; jsdom has no layout, so it starts from the window width.
  window.innerWidth = width;

  window.matchMedia = ((query: string): MediaQueryList => {
    const min = query.match(/min-width:\s*(\d+)px/);

    return {
      matches: min
        ? width >= Number(min[1])
        : false,
      media: query,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
      onchange: null,
    } as MediaQueryList;
  }) as typeof window.matchMedia;
}

export const laptopWidth = 1280;
export const phoneWidth = 390;
