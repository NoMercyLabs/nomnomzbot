<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// EmptyState in its four kinds and InlineNotice in its five tones: the two components that explain why a
// surface shows nothing or what the person should know.
import { useI18n } from 'vue-i18n';

import EmptyState from '@/components/layout/EmptyState.vue';
import InlineNotice from '@/components/layout/InlineNotice.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';

const { t } = useI18n();

const KINDS = ['none', 'noMatch', 'filtered', 'gated'] as const;
const TONES = ['info', 'warning', 'error', 'success', 'gated'] as const;
</script>

<template>
  <section data-catalog="empty-states" class="grid gap-4">
    <div class="grid grid-cols-2 gap-4 max-md:grid-cols-1">
      <PanelCard v-for="kind in KINDS" :key="kind" :title="t(`dashboard.catalog.feedback.kind.${kind}`)">
        <EmptyState
          :kind="kind"
          :title="t(`dashboard.catalog.feedback.empty.${kind}.title`)"
          :description="t(`dashboard.catalog.feedback.empty.${kind}.text`)"
        >
          <template #action>
            <!-- One primary per group: only the first-run empty state offers the one way forward. -->
            <Button :variant="kind === 'none' ? 'default' : 'outline'">
              {{ t(`dashboard.catalog.feedback.empty.${kind}.action`) }}
            </Button>
          </template>
        </EmptyState>
      </PanelCard>
    </div>
  </section>

  <section data-catalog="notices" class="grid gap-3">
    <InlineNotice v-for="tone in TONES" :key="tone" :tone="tone" :title="t(`dashboard.catalog.feedback.tone.${tone}`)">
      {{ t('dashboard.catalog.feedback.noticeText') }}
      <template #action>
        <Button variant="outline" size="sm">{{ t('dashboard.catalog.feedback.noticeAction') }}</Button>
      </template>
    </InlineNotice>
    <InlineNotice tone="error" compact>{{ t('dashboard.catalog.feedback.compactError') }}</InlineNotice>
    <InlineNotice tone="gated" compact>{{ t('dashboard.catalog.feedback.compactGated') }}</InlineNotice>
  </section>
</template>
