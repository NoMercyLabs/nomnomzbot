// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// jsdom lays nothing out, so it leaves out the browser calls that move the view. Every browser has them;
// without these a page that scrolls a chosen item into view throws in a test.
Element.prototype.scrollIntoView ??= (): void => {};

// jsdom has no text layout either; the charts (Unovis) measure their axis labels with these.
Object.defineProperty(SVGElement.prototype, 'getComputedTextLength', {
  value: () => 40,
  configurable: true,
});
Object.defineProperty(SVGElement.prototype, 'getBBox', {
  value: () => ({
    x: 0,
    y: 0,
    width: 40,
    height: 12,
  }),
  configurable: true,
});
