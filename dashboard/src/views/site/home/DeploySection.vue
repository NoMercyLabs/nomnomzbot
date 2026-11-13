<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Self-host or hosted. The license note under the cards must stay visible: earning money with
// NomNomzBot is reserved to NoMercy Labs, while running it for yourself, friends and community is free.
import { useI18n } from 'vue-i18n';
import Check from '~icons/nnz/check';

import InlineNotice from '@/components/layout/InlineNotice.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Badge } from '@/components/ui/badge';

import HomeSignIn from './HomeSignIn.vue';
import SectionHeading from './SectionHeading.vue';

type Option = { id: 'self' | 'hosted'; connect: boolean };

const options: Option[] = [
  { id: 'self', connect: true },
  { id: 'hosted', connect: false },
];
const points: string[] = ['point1', 'point2', 'point3'];
const { t } = useI18n();
</script>

<template>
  <section id="deploy" class="flex scroll-mt-6 flex-col gap-6">
    <SectionHeading
      :title="t('site.home.deploy.title')"
      :description="t('site.home.deploy.subtitle')"
    />
    <div class="grid gap-4 md:grid-cols-2">
      <PanelCard
        v-for="option in options"
        :key="option.id"
        :title="t(`site.home.deploy.${option.id}.title`)"
        :tier="option.connect ? 'lead' : 'quiet'"
        pad
      >
        <template #actions>
          <Badge variant="secondary">{{ t(`site.home.deploy.${option.id}.tag`) }}</Badge>
        </template>
        <ul class="flex flex-col gap-2">
          <li v-for="point in points" :key="point" class="flex items-start gap-2 text-sm">
            <Check class="mt-0.5 size-4 flex-none text-muted-foreground" aria-hidden="true" />
            {{ t(`site.home.deploy.${option.id}.${point}`) }}
          </li>
        </ul>
        <template v-if="option.connect" #footer>
          <HomeSignIn variant="outline" />
        </template>
      </PanelCard>
    </div>
    <InlineNotice tone="info">{{ t('site.home.deploy.note') }}</InlineNotice>
  </section>
</template>
