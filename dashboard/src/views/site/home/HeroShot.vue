<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The product shot under the hero: the real dashboard shell with its real sidebar, and one tab for each area.
// A tab changes the page the shell shows. The window is a still picture of sample data (see PreviewFrame).
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';

import LazyPreview from './preview/LazyPreview.vue';
import { areaPages, previewAreas, type PreviewArea } from './preview/previewAreas';
import { sampleChannel } from './sampleChannel';

const { t } = useI18n();
const area = ref<PreviewArea>('pipelines');

function choose(value: string | number): void {
  area.value = previewAreas.find((name) => name === value) ?? 'pipelines';
}
</script>

<template>
  <div class="flex w-full min-w-0 max-w-full flex-col gap-3 text-left" data-testid="hero-shot">
    <!-- A narrow screen scrolls the row sideways; wrapping drops one tab onto a line of its own. -->
    <Tabs class="min-w-0 max-w-full" :model-value="area" @update:model-value="choose">
      <!-- The strip keeps its tabs whole (frame.css), so this box is the part that scrolls. -->
      <div class="max-w-full overflow-x-auto scrollbar-none">
        <TabsList :aria-label="t('site.home.demo.label')" class="justify-start">
          <TabsTrigger v-for="name in previewAreas" :key="name" :value="name">
            {{ t(`site.home.areas.${name}.tab`) }}
          </TabsTrigger>
        </TabsList>
      </div>
    </Tabs>
    <LazyPreview
      eager
      shell
      :path="areaPages[area].path"
      :height="areaPages[area].height"
      :label="t(`site.home.areas.${area}.caption`)"
    />
    <p class="text-sm text-muted-foreground">
      {{ t(`site.home.areas.${area}.caption`) }}
      <span class="text-xs">{{ t('site.home.demo.sampleOf', { channel: sampleChannel.handle }) }}</span>
    </p>
  </div>
</template>
