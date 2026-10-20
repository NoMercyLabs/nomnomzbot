<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The request rules (mockup 15-music, panel 113-141) in three tabs: Requests (who may ask and the limits),
// Sources (the services and the banger playlist) and Blocked (the songs nobody may request).
// Edits stay in a draft until Save; Discard drops the draft without a request. Only changed fields are sent.
// Every tab stays mounted, so an edit made on one tab survives a visit to another.
import { computed, ref, watch } from 'vue';

import { useMusicConfig, useSaveMusicConfig, type MusicConfig, type MusicConfigChange } from '@/api/music';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import { changeOf, draftOf, draftValid, type RuleDraft } from './requestRules';
import RulesBlockedTab from './RulesBlockedTab.vue';
import RulesRequestsTab from './RulesRequestsTab.vue';
import RulesSourcesTab from './RulesSourcesTab.vue';

const tabs = ['requests', 'sources', 'blocked'] as const;
const tab = ref<(typeof tabs)[number]>('requests');

const { channelId } = useCurrentChannel();
const { can } = useCan();
const configQuery = useMusicConfig(channelId);
const save = useSaveMusicConfig(channelId);

const state = usePanelState(configQuery, (config: MusicConfig | null): boolean =>
  config === null);

const draft = ref<RuleDraft | null>(null);

watch(
  () => configQuery.data.value,
  (config: MusicConfig | null | undefined) => {
    draft.value = config
      ? draftOf(config)
      : null;
  },
  { immediate: true },
);

const change = computed(() => {
  const config = configQuery.data.value;
  const edits = draft.value;

  return config && edits
    ? changeOf(config, edits)
    : ({} as const satisfies MusicConfigChange);
});
const valid = computed<boolean>(() => draft.value === null || draftValid(draft.value));
const dirty = computed<boolean>(() => valid.value && Object.keys(change.value).length > 0);

// The header switch saves on its own; the draft follows it so Save never sends it a second time.
function setRequestsOpen(open: boolean): void {
  if (draft.value) {
    draft.value.requestsOpen = open;
  }

  save.mutate({ requestsOpen: open });
}

function saveRules(): void {
  if (dirty.value) {
    save.mutate(change.value);
  }
}

function discard(): void {
  const config = configQuery.data.value;

  draft.value = config
    ? draftOf(config)
    : null;
}
</script>

<template>
  <PanelCard
    tier="quiet"
    pad
    :title="$t('dashboard.music.rules.title')"
    :state="state"
    :empty-text="$t('dashboard.music.rules.empty')"
    :error-text="$t('dashboard.music.rules.error')"
    @retry="configQuery.refetch()"
  >
    <!-- The master switch sits in the header bar and saves at once, like every other panel's on/off. -->
    <template v-if="draft" #actions>
      <LabeledSwitch
        :label="$t('dashboard.music.rules.open')"
        action="music:config:write"
        :model-value="draft.requestsOpen"
        data-setting="requests-open"
        @update:model-value="setRequestsOpen"
      />
    </template>
    <Tabs v-if="draft" v-model="tab" class="gap-4">
      <TabsList class="w-full">
        <TabsTrigger v-for="name in tabs" :key="name" :value="name" :data-rules-tab="name">
          {{ $t(`dashboard.music.rules.tabs.${name}`) }}
        </TabsTrigger>
      </TabsList>
      <TabsContent value="requests" force-mount>
        <RulesRequestsTab v-model="draft" :valid="valid" />
      </TabsContent>
      <TabsContent value="sources" force-mount>
        <RulesSourcesTab v-model="draft" />
      </TabsContent>
      <TabsContent value="blocked" force-mount>
        <RulesBlockedTab />
      </TabsContent>
    </Tabs>
    <template v-if="state === 'ready' && can('music:config:write')" #footer>
      <Button variant="outline" size="sm" :disabled="!dirty" @click="discard">{{
        $t("dashboard.music.rules.discard")
      }}</Button>
      <Button size="sm" :disabled="!dirty" @click="saveRules">{{
        $t("dashboard.music.rules.save")
      }}</Button>
    </template>
  </PanelCard>
</template>
