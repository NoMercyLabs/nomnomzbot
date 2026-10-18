<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The platform's defaults: what a channel follows until it sets its own value. One section per kind of
// default, each its own PanelCard component (a new kind is one component added below). Every change shows
// the counted blast radius first; the queries share the ['admin', 'platform-defaults'] prefix.
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';

import PlatformActionDefaults from './components/PlatformActionDefaults.vue';
import PlatformBuiltinReplyDefaults from './components/PlatformBuiltinReplyDefaults.vue';
import PlatformEventResponseDefaults from './components/PlatformEventResponseDefaults.vue';
</script>

<template>
  <PageHeader
    :title="$t('dashboard.admin.platformDefaults.title')"
    :crumb="$t('dashboard.admin.platformDefaults.crumb')"
  />
  <PageBody>
    <p class="text-sm text-muted-foreground">
      {{ $t("dashboard.admin.platformDefaults.explain") }}
    </p>
    <PlatformActionDefaults />
    <PlatformBuiltinReplyDefaults />
    <PlatformEventResponseDefaults />
  </PageBody>
</template>
