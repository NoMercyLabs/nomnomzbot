// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { beforeEach, vi } from 'vitest';

// The real frame (DashboardLayout) owns the sticky action footer that a page's actions move into below the
// tablet width (PageActionFooter, PageCreateButton). A spec that mounts one page has no frame, and jsdom has
// no media query, so it always lays out as a phone; this stands the frame's footer behind every test.
beforeEach(() => {
  document.body.insertAdjacentHTML('beforeend', '<div data-slot="frame-action-footer"></div>');
});

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

// The real frame (DashboardLayout) mounts the one TooltipProvider, so a row never carries its own. A spec
// that mounts a single row has no frame; this stands the frame's provider behind every Tooltip so the row
// still renders. structure.spec guards that only the frames mount a provider in the app.
vi.mock('@/components/ui/tooltip/Tooltip.vue', async () => {
  const { defineComponent, h } = await import('vue');
  const actual = await vi.importActual<typeof import('@/components/ui/tooltip/Tooltip.vue')>(
    '@/components/ui/tooltip/Tooltip.vue',
  );
  const provider = await vi.importActual<typeof import('@/components/ui/tooltip/TooltipProvider.vue')>(
    '@/components/ui/tooltip/TooltipProvider.vue',
  );

  return {
    default: defineComponent({
      name: 'FramedTooltip',
      inheritAttrs: false,
      setup: (_, { attrs, slots }) => () =>
        h(provider.default, { delayDuration: 150 }, () =>
          h(actual.default, attrs, slots)),
    }),
  };
});
