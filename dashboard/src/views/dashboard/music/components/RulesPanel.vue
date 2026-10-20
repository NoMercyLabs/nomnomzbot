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
import { useI18n } from 'vue-i18n';

import {
  useMusicConfig,
  useMusicDefaults,
  useSaveMusicConfig,
  type MusicConfig,
  type MusicConfigChange,
} from '@/api/music';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import RotateLeft from '~icons/nnz/rotate-left';

import { changeOf, draftOf, draftValid, type RuleDraft } from './requestRules';
import { resetChange } from './rulesReset';
import RulesBlockedTab from './RulesBlockedTab.vue';
import RulesRequestsTab from './RulesRequestsTab.vue';
import RulesSourcesTab from './RulesSourcesTab.vue';

const tabs = ['requests', 'sources', 'blocked'] as const;
const tab = ref<(typeof tabs)[number]>('requests');

const { channelId } = useCurrentChannel();
const { can } = useCan();
const configQuery = useMusicConfig(channelId);
const save = useSaveMusicConfig(channelId);
const defaultsQuery = useMusicDefaults(channelId);
const { t } = useI18n();
const resetOpen = ref(false);

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

// What a reset writes back: the rules that differ from the platform defaults the bot reports.
const resetChanges = computed(() => {
  const config = configQuery.data.value;
  const standard = defaultsQuery.data.value;

  return config && standard
    ? resetChange(config, standard)
    : {} as const satisfies MusicConfigChange;
});
const resetEffects = computed<string[]>(() =>
  Object.keys(resetChanges.value).map((field): string =>
    t(`dashboard.music.rules.reset.fields.${field}`)),
);

function resetRules(): void {
  resetOpen.value = false;
  save.mutate(resetChanges.value);
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
    <ConfirmDialog
      v-model:open="resetOpen"
      :title="$t('dashboard.music.rules.reset.title')"
      :description="$t('dashboard.music.rules.reset.body')"
      :consequences="resetEffects"
      :action="$t('dashboard.music.rules.reset.action')"
      @confirm="resetRules"
    />
    <template v-if="state === 'ready' && can('music:config:write')" #footer>
      <Button
        v-if="resetEffects.length > 0"
        variant="outline"
        size="sm"
        :disabled="save.isPending.value"
        @click="resetOpen = true"
      >
        <RotateLeft aria-hidden="true" />
        {{ $t("dashboard.music.rules.reset.button") }}
      </Button>
      <span class="mr-auto" />
      <Button variant="outline" size="sm" :disabled="!dirty" @click="discard">{{
        $t("dashboard.music.rules.discard")
      }}</Button>
      <Button size="sm" :disabled="!dirty" @click="saveRules">{{
        $t("dashboard.music.rules.save")
      }}</Button>
    </template>
  </PanelCard>
</template>
