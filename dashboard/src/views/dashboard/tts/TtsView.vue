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
import { useRouter } from 'vue-router';

import { useTtsConfig, useTtsVoices, type TtsConfig } from '@/api/tts';
import type { BoardPanel } from '@/components/layout/board';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs from '@/components/layout/PageTabs.vue';
import type { PageTab } from '@/components/layout/pageTab';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePageView } from '@/composables/usePageView';
import { keysOf } from '@/lib/array';
import { usePanelState } from '@/composables/usePanelState';
import { useProviderName } from '@/composables/useProviderName';

import HistoryPanel from './HistoryPanel.vue';
import KeysPanel from './KeysPanel.vue';
import LexiconPanel from './LexiconPanel.vue';
import RulesPanel from './RulesPanel.vue';
import SpeakingPanel from './SpeakingPanel.vue';
import TtsBoard from './TtsBoard.vue';
import TtsRewardsPanel from './TtsRewardsPanel.vue';
import VoicesTab from './VoicesTab.vue';

const { t } = useI18n();
const router = useRouter();
const providerName = useProviderName();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const config = useTtsConfig(channelId);
// The same first page the Voices tab loads, so the voice reads by its name here too.
const catalog = useTtsVoices(channelId, () => ({ q: '' }));

// Each tab needs the read key of the endpoint it shows.
const tabKeys = {
  overview: 'tts:config:read',
  voices: 'tts:voice:read',
  rules: 'tts:config:read',
  pronunciation: 'tts:config:read',
  keys: 'tts:config:write',
} as const satisfies Record<string, string>;
const VIEWS = keysOf(tabKeys);

type TtsTab = (typeof VIEWS)[number];
const allTabs = computed<PageTab<TtsTab>[]>(() => [
  { value: 'overview', label: t('dashboard.tts.tabs.overview') },
  { value: 'voices', label: t('dashboard.tts.tabs.voices') },
  { value: 'rules', label: t('dashboard.tts.tabs.rules') },
  { value: 'pronunciation', label: t('dashboard.tts.tabs.pronunciation') },
  { value: 'keys', label: t('dashboard.tts.tabs.keys') },
]);

function canRead(tab: PageTab<TtsTab>): boolean {
  return can(tabKeys[tab.value]);
}

const tabs = computed(() => allTabs.value.filter(canRead));
const view = usePageView(VIEWS, 'view', () =>
  tabs.value.map((tab) => tab.value));

const settings = computed<TtsConfig | null>(() => config.data.value ?? null);

const settingsState = usePanelState<TtsConfig | null>(
  config,
  (data: TtsConfig | null) => data === null,
);

const voiceDetail = computed<string>(() => {
  const current = settings.value;

  if (current === null)
    return '';

  const provider = providerName(current.defaultProvider);

  if (current.defaultVoiceId) {
    const loaded = (catalog.data.value?.pages ?? []).flatMap((page) => page.voices);
    const known = loaded.find((voice) => voice.id === current.defaultVoiceId);

    return t('dashboard.tts.settings.voice.detail', {
      voice: known?.displayName ?? current.defaultVoiceId,
      provider,
    });
  }

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

const overviewPanels = computed(
  () =>
    [
      { id: 'history', label: t('dashboard.tts.history.title') },
      { id: 'settings', label: t('dashboard.tts.settings.title') },
    ] as const satisfies readonly BoardPanel[],
);
const rulesPanels = computed(
  () =>
    [
      { id: 'speaking', label: t('dashboard.tts.speaking.title') },
      { id: 'rules', label: t('dashboard.tts.rules.title') },
      { id: 'rewards', label: t('dashboard.tts.rewards.title') },
    ] as const satisfies readonly BoardPanel[],
);
const keysPanels = computed(
  () =>
    [{ id: 'keys', label: t('dashboard.tts.keys.title') }] as const satisfies readonly BoardPanel[],
);
const pronunciationPanels = computed(
  () =>
    [
      { id: 'lexicon', label: t('dashboard.tts.lexicon.title') },
    ] as const satisfies readonly BoardPanel[],
);
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
    <template #bar><PageTabs :tabs="tabs" /></template>
  </PageHeader>

  <TtsBoard
    v-if="view === 'overview' && can('tts:config:read')"
    :panels="overviewPanels"
    :spans="{ history: 7, settings: 5 }"
    :rows="1"
    phone-first="settings"
  >
    <template #history>
      <HistoryPanel />
    </template>
    <template #settings>
      <PanelCard
        :title="$t('dashboard.tts.settings.title')"
        :description="$t('dashboard.tts.settings.description')"
        :state="settingsState"
        :error-text="$t('dashboard.tts.settings.error')"
        @retry="config.refetch()"
      >
        <RowGroup v-if="settings">
          <SummaryRow
            data-settings-row
            pick
            chevron
            :title="$t('dashboard.tts.settings.voice.title')"
            :detail="voiceDetail"
            @click="view = 'voices'"
          />
          <SummaryRow
            data-settings-row
            pick
            chevron
            :title="$t('dashboard.tts.settings.who.title')"
            :detail="
              $t('dashboard.tts.settings.who.detail', { permission: settings.minPermission })
            "
            @click="view = 'rules'"
          />
          <SummaryRow
            data-settings-row
            pick
            chevron
            :title="$t('dashboard.tts.settings.limits.title')"
            :detail="limitsDetail"
            @click="view = 'rules'"
          />
          <SummaryRow
            data-settings-row
            pick
            chevron
            :title="$t('dashboard.tts.settings.words.title')"
            :detail="wordsDetail"
            @click="view = 'rules'"
          />
          <SummaryRow
            data-settings-row
            data-required-overlays-link
            pick
            chevron
            :title="$t('dashboard.tts.settings.overlay.title')"
            :detail="$t('dashboard.tts.settings.overlay.detail')"
            @click="router.push({ name: 'dashboard-obs', hash: '#required-overlays' })"
          />
        </RowGroup>
      </PanelCard>
    </template>
  </TtsBoard>

  <VoicesTab v-else-if="view === 'voices' && can('tts:voice:read')" />

  <!-- Two columns at every laptop width: the speaking settings on the left, the rules on the right. The
       pronunciation lexicon and the keys have their own tabs, so no panel scrolls inside the page. -->
  <TtsBoard
    v-else-if="view === 'rules' && can('tts:config:read')"
    :panels="rulesPanels"
    :spans="{ speaking: 6, rules: 6, rewards: 12 }"
    :rows="2"
  >
    <template #speaking><SpeakingPanel /></template>
    <template #rules><RulesPanel /></template>
    <template #rewards><TtsRewardsPanel /></template>
  </TtsBoard>

  <TtsBoard
    v-else-if="view === 'pronunciation' && can('tts:config:read')"
    :panels="pronunciationPanels"
    :spans="{ lexicon: 12 }"
    :rows="1"
  >
    <template #lexicon><LexiconPanel /></template>
  </TtsBoard>

  <TtsBoard
    v-else-if="view === 'keys' && can('tts:config:write')"
    :panels="keysPanels"
    :spans="{ keys: 12 }"
    :rows="1"
  >
    <template #keys><KeysPanel /></template>
  </TtsBoard>
</template>
