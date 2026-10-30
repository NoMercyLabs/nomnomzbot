<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The TTS tab of a channel console: the figures of the last 24 hours on top, then the last 50 clips with their
// synthesis time and failures, from the diagnostics `tts` section. One read feeds both panels. When the read
// fails, one error with a retry shows and the clips panel stays out, so the error is not said twice.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useChannelDiagnostics, type ChannelTtsClip } from '@/api/adminDiagnostics';
import PanelCard from '@/components/layout/PanelCard.vue';
import { usePanelState } from '@/composables/usePanelState';

import ChannelTtsFigures from './ChannelTtsFigures.vue';
import ChannelTtsTable from './ChannelTtsTable.vue';

const props = defineProps<{ channelId: string }>();

const { t } = useI18n();

const diagnostics = useChannelDiagnostics(() => props.channelId, ['tts']);

const tts = computed(() => diagnostics.data.value?.tts ?? null);
const clips = computed<ChannelTtsClip[]>(() => tts.value?.clips ?? []);

const figuresState = usePanelState(diagnostics, (data) =>
  (data?.tts ?? null) === null);
const clipsState = usePanelState(diagnostics, (data) =>
  (data?.tts?.clips.length ?? 0) === 0);
</script>

<template>
  <div data-tts-tab class="grid gap-3">
    <PanelCard
      pad
      :title="t('dashboard.admin.console.tts.figuresTitle')"
      :description="t('dashboard.admin.console.tts.figuresDescription')"
      :state="figuresState"
      :empty-text="t('dashboard.admin.console.tts.error')"
      :error-text="t('dashboard.admin.console.tts.error')"
      @retry="diagnostics.refetch()"
    >
      <ChannelTtsFigures v-if="tts" :last24-hours="tts.last24Hours" />
    </PanelCard>
    <PanelCard
      v-if="figuresState !== 'error'"
      :title="t('dashboard.admin.console.tts.title')"
      :description="t('dashboard.admin.console.tts.description')"
      :state="clipsState"
      :empty-text="t('dashboard.admin.console.tts.empty')"
      :error-text="t('dashboard.admin.console.tts.error')"
    >
      <ChannelTtsTable :clips="clips" />
    </PanelCard>
  </div>
</template>
