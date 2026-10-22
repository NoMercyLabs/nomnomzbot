// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
