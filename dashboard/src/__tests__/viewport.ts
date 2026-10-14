// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
