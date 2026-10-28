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

import { createApp } from 'vue';

import './styles/main.css';
import { applyAccent, defaultAccent } from './lib/accent';
import { i18n, startLocale } from './i18n';
import { createSiteRouter } from './router/siteOnly';
import { followSiteTitle } from './router/siteTitle';
import SiteApp from './SiteApp.vue';

// The entry of the public site build (site.html). It starts no bot connection: no query client, no
// session, no live hub, no service worker. The dashboard entry is main.ts.
applyAccent(defaultAccent);

const app = createApp(SiteApp);

const router = createSiteRouter();

app.use(i18n);
app.use(router);
followSiteTitle(router);

await startLocale();

app.mount('#app');
