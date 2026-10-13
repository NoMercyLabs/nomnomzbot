<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useTtsConfig, useTtsOverlay, type TtsConfig } from '@/api/tts';
import BoardCell from '@/components/layout/BoardCell.vue';
import BoardGrid from '@/components/layout/BoardGrid.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs, { type PageTab } from '@/components/layout/PageTabs.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePageView } from '@/composables/usePageView';
import { usePanelState } from '@/composables/usePanelState';
import { useProviderName } from '@/composables/useProviderName';

import VoicesTab from './VoicesTab.vue';

const { t, locale } = useI18n();
const providerName = useProviderName();
const { channelId } = useCurrentChannel();
const config = useTtsConfig(channelId);
const overlay = useTtsOverlay(channelId);

const tabs = computed<PageTab[]>(() => [
  { value: 'overview', label: t('dashboard.tts.tabs.overview') },
  { value: 'voices', label: t('dashboard.tts.tabs.voices') },
  { value: 'rules', label: t('dashboard.tts.tabs.rules') },
]);
const view = usePageView(() => tabs.value.map((tab: PageTab) => tab.value));

const settings = computed<TtsConfig | null>(
  () => config.data.value ?? null,
);

const settingsState = usePanelState<TtsConfig | null>(
  config,
  (data: TtsConfig | null) => data === null,
);

const voiceDetail = computed<string>(() => {
  const current = settings.value;

  if (current === null)
    return '';

  const provider = providerName(current.defaultProvider);

  if (current.defaultVoiceId)
    return t('dashboard.tts.settings.voice.detail', { voice: current.defaultVoiceId, provider });

  return t(
    current.followsPlatformDefaultVoice
      ? 'dashboard.tts.settings.voice.platform'
      : 'dashboard.tts.settings.voice.none',
    { provider },
  );
});

const limitsDetail = computed<string>(() => {
  const current = settings.value;

  if (current === null)
    return '';

  const count = String(current.maxCharacters);
  const bots: string = t(
    current.skipBotMessages
      ? 'dashboard.tts.settings.limits.skipBots'
      : 'dashboard.tts.settings.limits.readBots',
  );

  return `${t('dashboard.tts.settings.limits.detail', { count })} · ${bots}`;
});

const wordsDetail = computed<string>(() =>
  t(
    settings.value?.profanityCensorEnabled
      ? 'dashboard.tts.settings.words.censored'
      : 'dashboard.tts.settings.words.uncensored',
  ),
);

const overlayDetail = computed<string>(() => {
  if (overlay.isError.value)
    return t('dashboard.tts.settings.overlay.error');

  const ranAt = overlay.data.value?.lastRanAt;

  if (overlay.data.value === undefined || overlay.data.value === null)
    return '';

  if (!ranAt)
    return t('dashboard.tts.settings.overlay.never');

  return t('dashboard.tts.settings.overlay.lastUsed', {
    time: new Date(ranAt).toLocaleString(locale.value),
  });
});
</script>

<template>
  <PageHeader :title="$t('dashboard.tts.title')">
    <template #status>
      <StatusBadge
        v-if="settings"
        :tone="settings.isEnabled ? 'ok' : 'off'"
        :label="$t(settings.isEnabled ? 'dashboard.tts.on' : 'dashboard.tts.off')"
      />
    </template>
    <template #tabs><PageTabs :tabs="tabs" /></template>
  </PageHeader>

  <BoardGrid v-if="view === 'overview'" :rows="1">
    <BoardCell :span="7">
      <PanelCard
        :title="$t('dashboard.tts.history.title')"
        :description="$t('dashboard.tts.history.description')"
        tier="lead"
        state="empty"
        :empty-text="$t('dashboard.tts.history.empty')"
      />
    </BoardCell>
    <BoardCell :span="5">
      <PanelCard
        :title="$t('dashboard.tts.settings.title')"
        :state="settingsState"
        :error-text="$t('dashboard.tts.settings.error')"
        @retry="config.refetch()"
      >
        <RowGroup v-if="settings">
          <SummaryRow
            data-settings-row
            pick
            :title="$t('dashboard.tts.settings.voice.title')"
            :detail="voiceDetail"
            @click="view = 'voices'"
          />
          <SummaryRow
            data-settings-row
            pick
            :title="$t('dashboard.tts.settings.who.title')"
            :detail="
              $t('dashboard.tts.settings.who.detail', { permission: settings.minPermission })
            "
            @click="view = 'rules'"
          />
          <SummaryRow
            data-settings-row
            pick
            :title="$t('dashboard.tts.settings.limits.title')"
            :detail="limitsDetail"
            @click="view = 'rules'"
          />
          <SummaryRow
            data-settings-row
            pick
            :title="$t('dashboard.tts.settings.words.title')"
            :detail="wordsDetail"
            @click="view = 'rules'"
          />
          <SummaryRow
            data-settings-row
            :title="$t('dashboard.tts.settings.overlay.title')"
            :detail="overlayDetail"
          />
        </RowGroup>
      </PanelCard>
    </BoardCell>
  </BoardGrid>

  <VoicesTab v-else-if="view === 'voices'" />

  <BoardGrid v-else :rows="1">
    <BoardCell>
      <PanelCard
        :title="$t('dashboard.tts.rules.title')"
        state="empty"
        :empty-text="$t('dashboard.tts.rules.empty')"
      />
    </BoardCell>
  </BoardGrid>
</template>
