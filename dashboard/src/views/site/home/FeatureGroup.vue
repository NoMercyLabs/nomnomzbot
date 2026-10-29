<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One feature group: a short text column and a wide picture of the real dashboard page below it. The only
// action is one outline link to the page's source, so the group has one thing to do and it is not the accent.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { Button } from '@/components/ui/button';

import { groupPages } from './preview/previewAreas';
import type { PreviewArea } from './preview/previewAreas';
import ShowcaseImage from './ShowcaseImage.vue';

const props = defineProps<{ area: PreviewArea }>();

const { t } = useI18n();
const page = computed(() => groupPages[props.area]);
const sourceUrl = computed(() => `${t('site.home.githubUrl')}/tree/master/dashboard/src/views/dashboard/${page.value.source}`);
</script>

<template>
  <article :data-feature-group="area" class="flex min-w-0 flex-col gap-4">
    <div class="flex flex-wrap items-end justify-between gap-x-6 gap-y-3">
      <div class="flex max-w-2xl flex-col gap-1">
        <h3 class="text-xl font-semibold">{{ t(`site.home.areas.${area}.title`) }}</h3>
        <p class="text-base text-muted-foreground">{{ t(`site.home.areas.${area}.text`) }}</p>
      </div>
      <Button as-child variant="outline">
        <a :href="sourceUrl" target="_blank" rel="noopener">{{ t('site.home.features.source') }}</a>
      </Button>
    </div>
    <ShowcaseImage :area="area" kind="group" :label="t(`site.home.areas.${area}.caption`)" />
    <p class="text-sm text-muted-foreground">{{ t(`site.home.areas.${area}.caption`) }}</p>
  </article>
</template>
