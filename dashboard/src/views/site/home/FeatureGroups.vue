<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The feature list: eight groups from the product's feature inventory, each a short line and one preview.
import { useI18n } from 'vue-i18n';

import FeaturePreview from './FeaturePreview.vue';
import { featureGroups } from './sampleChannel';

const { t } = useI18n();
</script>

<template>
  <section id="features" class="flex scroll-mt-6 flex-col gap-8">
    <div class="flex flex-col gap-2">
      <h2 class="text-2xl font-semibold">{{ t('site.home.features.title') }}</h2>
      <p class="text-base text-muted-foreground">{{ t('site.home.features.sampleNote') }}</p>
    </div>
    <div class="grid gap-6 md:grid-cols-2">
      <!-- Card radius 20 with 8px padding, so the preview inside is 20 - 8 = 12. -->
      <article
        v-for="group in featureGroups"
        :key="group"
        :data-feature-group="group"
        class="flex min-w-0 flex-col gap-3 rounded-lg border border-border bg-card p-2"
      >
        <div class="flex flex-col gap-1 px-3 pt-3">
          <h3 class="text-base font-semibold">{{ t(`site.home.features.${group}.title`) }}</h3>
          <p class="text-sm text-muted-foreground">{{ t(`site.home.features.${group}.line`) }}</p>
        </div>
        <FeaturePreview :group="group" />
      </article>
    </div>
  </section>
</template>
