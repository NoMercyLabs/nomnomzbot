<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The channel's Guest Star settings. The form holds a draft of what Twitch holds; Save sends the whole draft in one
// change and stays off until the draft differs from Twitch.
import { computed, reactive, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useGuestStarSettings, useSaveGuestStarSettings, type GuestStarSettings } from '@/api/guestStar';
import ActionRow from '@/components/layout/ActionRow.vue';
import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import OptionSelect, { type Choice } from '@/components/layout/OptionSelect.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import { useGuestStarFailure } from '../guestStarFailure';

const layouts = ['TILED_LAYOUT', 'SCREENSHARE_LAYOUT', 'HORIZONTAL_LAYOUT', 'VERTICAL_LAYOUT'] as const;

const { t } = useI18n();
const { can } = useCan();
const { notify } = useGuestStarFailure();
const { channelId } = useCurrentChannel();

const settings = useGuestStarSettings(channelId);
const save = useSaveGuestStarSettings(channelId);
const state = usePanelState(settings);
const canWrite = computed(() => can('guest-star:write'));

const draft = reactive({
  isModeratorSendLiveEnabled: false,
  slotCount: 1,
  isBrowserSourceAudioEnabled: false,
  groupLayout: '',
  regenerateBrowserSources: false,
});

// Twitch reads the layout back as TILED but takes TILED_LAYOUT on write; the form holds the write name.
function layoutName(held: string): string {
  return held === '' || held.endsWith('_LAYOUT')
    ? held
    : `${held}_LAYOUT`;
}

function load(held: GuestStarSettings): void {
  draft.isModeratorSendLiveEnabled = held.isModeratorSendLiveEnabled;
  draft.slotCount = Number(held.slotCount);
  draft.isBrowserSourceAudioEnabled = held.isBrowserSourceAudioEnabled;
  draft.groupLayout = layoutName(held.groupLayout);
  draft.regenerateBrowserSources = false;
}

watch(() => settings.data.value, (held) => {
  if (held !== undefined) {
    load(held);
  }
}, { immediate: true });

const layoutChoices = computed<Choice[]>(() =>
  layouts.map((layout) => ({ value: layout, label: t(`dashboard.settings.guestStar.settings.layouts.${layout}`) })));

const layoutLabel = computed(() => {
  const match = layoutChoices.value.find((choice) => choice.value === draft.groupLayout);

  return match?.label ?? draft.groupLayout;
});

const dirty = computed<boolean>(() => {
  const held = settings.data.value;

  return held !== undefined && (
    draft.regenerateBrowserSources ||
    draft.isModeratorSendLiveEnabled !== held.isModeratorSendLiveEnabled ||
    draft.slotCount !== Number(held.slotCount) ||
    draft.isBrowserSourceAudioEnabled !== held.isBrowserSourceAudioEnabled ||
    draft.groupLayout !== layoutName(held.groupLayout)
  );
});

function submit(): void {
  save.mutate({ ...draft }, { onError: notify });
}
</script>

<template>
  <PanelCard
    :title="$t('dashboard.settings.guestStar.settings.title')"
    :state="state"
    :error-text="$t('dashboard.settings.guestStar.loadError')"
    data-panel="settings"
    @retry="settings.refetch()"
  >
    <RowGroup>
      <ActionRow
        :title="$t('dashboard.settings.guestStar.settings.moderatorSendLive')"
        :description="$t('dashboard.settings.guestStar.settings.moderatorSendLiveHelp')"
      >
        <GatedSwitch
          v-model="draft.isModeratorSendLiveEnabled"
          action="guest-star:write"
          data-setting="moderatorSendLive"
          :aria-label="$t('dashboard.settings.guestStar.settings.moderatorSendLive')"
        />
      </ActionRow>
      <ActionRow
        :title="$t('dashboard.settings.guestStar.settings.browserAudio')"
        :description="$t('dashboard.settings.guestStar.settings.browserAudioHelp')"
      >
        <GatedSwitch
          v-model="draft.isBrowserSourceAudioEnabled"
          action="guest-star:write"
          data-setting="browserAudio"
          :aria-label="$t('dashboard.settings.guestStar.settings.browserAudio')"
        />
      </ActionRow>
      <ActionRow
        :title="$t('dashboard.settings.guestStar.settings.slotCount')"
        :description="$t('dashboard.settings.guestStar.settings.slotCountHelp')"
      >
        <Input
          v-if="canWrite"
          type="number"
          min="1"
          class="w-28 text-end"
          data-setting="slotCount"
          :aria-label="$t('dashboard.settings.guestStar.settings.slotCount')"
          :model-value="draft.slotCount"
          @update:model-value="draft.slotCount = Number($event)"
        />
        <span v-else class="text-sm tabular-nums" data-setting="slotCount">{{ draft.slotCount }}</span>
      </ActionRow>
      <ActionRow :title="$t('dashboard.settings.guestStar.settings.groupLayout')">
        <OptionSelect
          v-if="canWrite"
          v-model="draft.groupLayout"
          data-setting="groupLayout"
          :options="layoutChoices"
        />
        <span v-else class="text-sm" data-setting="groupLayout">{{ layoutLabel }}</span>
      </ActionRow>
      <ActionRow
        :title="$t('dashboard.settings.guestStar.settings.regenerate')"
        :description="$t('dashboard.settings.guestStar.settings.regenerateHelp')"
      >
        <GatedSwitch
          v-model="draft.regenerateBrowserSources"
          action="guest-star:write"
          data-setting="regenerate"
          :aria-label="$t('dashboard.settings.guestStar.settings.regenerate')"
        />
      </ActionRow>
    </RowGroup>
    <template v-if="canWrite" #footer>
      <Button data-save-settings :disabled="!dirty || save.isPending.value" @click="submit">
        {{ $t("dashboard.settings.guestStar.settings.save") }}
      </Button>
    </template>
  </PanelCard>
</template>
