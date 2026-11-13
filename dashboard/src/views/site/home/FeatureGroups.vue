<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The feature list: one group for each product area, each with a wide preview of its real dashboard page.
import { useI18n } from 'vue-i18n';

import FeatureGroup from './FeatureGroup.vue';
import { previewAreas } from './preview/previewAreas';
import SectionHeading from './SectionHeading.vue';

const { t } = useI18n();
</script>

<template>
  <section id="features" class="flex scroll-mt-6 flex-col gap-16">
    <SectionHeading
      :title="t('site.home.features.title')"
      :description="t('site.home.features.sampleNote')"
    />
    <FeatureGroup v-for="area in previewAreas" :key="area" :area="area" />
  </section>
</template>
