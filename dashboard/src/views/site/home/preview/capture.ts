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

import '@/styles/main.css';

import { applyAccent, defaultAccent } from '@/lib/accent';
import { startLocale } from '@/i18n';

import { startFakeApi } from './fakeApi';
import { createPreviewApp, type PreviewApp } from './previewApp';
import { areaPages, groupPages, previewAreas, type PreviewArea, type ShowcaseKind } from './previewAreas';

// Development only (capture.html). Draws the page of one showcase view into the whole window, with the same sample
// answers the home page uses, so a screenshot of the window is the picture the home page then shows.
// `window.showcase.draw(area, kind)` draws a view and answers when it has settled; `window.showcase.views` lists them.
export type CaptureKind = ShowcaseKind;

const settleMs = 3500;
let current: PreviewApp | null = null;

async function draw(area: PreviewArea, kind: CaptureKind): Promise<string> {
  current?.app.unmount();

  const page = kind === 'hero'
    ? areaPages[area]
    : groupPages[area];

  // The hero view carries the sidebar, like the picture under the hero; the group views do not, like theirs.
  current = createPreviewApp(page.path, kind === 'hero');
  current.app.mount('#capture');
  await current.router.isReady();

  // A page may focus its search box when it opens; a still picture has no blinking cursor.
  const blur = window.setInterval(() => {
    if (document.activeElement instanceof HTMLElement) {
      document.activeElement.blur();
    }
  }, 250);

  await new Promise((resolve) => window.setTimeout(resolve, settleMs));
  window.clearInterval(blur);

  return page.path;
}

applyAccent(defaultAccent);
startFakeApi();
await startLocale();

const views = previewAreas.flatMap((area) => [
  { area, kind: 'hero' as const },
  { area, kind: 'group' as const },
]);

Object.assign(window, { showcase: { draw, views } });
document.documentElement.dataset.captureReady = 'true';
