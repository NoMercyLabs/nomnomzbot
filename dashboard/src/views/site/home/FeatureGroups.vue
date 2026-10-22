<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The feature list: one group for each product area, each with a wide preview of its real dashboard page.
import { useI18n } from 'vue-i18n';

import FeatureGroup from './FeatureGroup.vue';
import { previewAreas } from './preview/previewAreas';

const { t } = useI18n();
</script>

<template>
  <section id="features" class="flex scroll-mt-6 flex-col gap-16">
    <div class="flex flex-col gap-2">
      <h2 class="text-2xl font-semibold">{{ t('site.home.features.title') }}</h2>
      <p class="text-base text-muted-foreground">{{ t('site.home.features.sampleNote') }}</p>
    </div>
    <FeatureGroup v-for="area in previewAreas" :key="area" :area="area" />
  </section>
</template>
