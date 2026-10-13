// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { createApp, effectScope } from 'vue'
import { createPinia } from 'pinia'
import { VueQueryPlugin } from '@tanstack/vue-query'

import App from './App.vue'
import './styles/main.css'
import { queryClient } from './api/queryClient'
import { i18n, startLocale } from './i18n'
import { useReadability } from './composables/useReadability'
import { applyAccent, defaultAccent } from './lib/accent'
import router from './router'

// The approved pink stands in until the channel color is known.
applyAccent(defaultAccent)
// The reading options of this device apply on every page, before the first one shows.
effectScope(true).run(useReadability)

const app = createApp(App)

app.use(createPinia())
// The library adds its Vue DevTools panel in development builds only.
app.use(VueQueryPlugin, { queryClient, enableDevtoolsV6Plugin: true })
app.use(i18n)
app.use(router)

await startLocale()
app.mount('#app')
