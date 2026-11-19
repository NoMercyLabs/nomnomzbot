<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The guests in the slots of the running session. Each row shows the guest by display name and the real state of
// that slot (audio, video, live, volume), as Twitch holds it after the last change.
import { computed, ref } from 'vue';

import {
  useRemoveGuestStarSlot,
  useSaveGuestStarSlotSettings,
  type GuestStarGuest,
  type GuestStarSlotChange,
} from '@/api/guestStar';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import { useGuestStarFailure } from '../guestStarFailure';
import AssignDialog from './AssignDialog.vue';
import MoveDialog from './MoveDialog.vue';

type Toggle = {
  key: 'audio' | 'video' | 'live';
  checked: boolean;
  change: GuestStarSlotChange;
};

const props = defineProps<{ sessionId: string; guests: readonly GuestStarGuest[] }>();

const { can } = useCan();
const { notify } = useGuestStarFailure();
const { channelId } = useCurrentChannel();

const remove = useRemoveGuestStarSlot(channelId);
const change = useSaveGuestStarSlotSettings(channelId);
const canWrite = computed(() => can('guest-star:write'));
const state = usePanelState(
  {
    data: computed(() => props.guests),
    isPending: ref(false),
    isError: ref(false),
  },
  (list: readonly GuestStarGuest[]) => list.length === 0,
);

const assigning = ref(false);
const moving = ref<GuestStarGuest | null>(null);
const removing = ref<GuestStarGuest | null>(null);
const reinvite = ref(false);

function togglesOf(guest: GuestStarGuest): Toggle[] {
  return [
    {
      key: 'audio',
      checked: guest.audioSettings.isHostEnabled,
      change: { isAudioEnabled: !guest.audioSettings.isHostEnabled },
    },
    {
      key: 'video',
      checked: guest.videoSettings.isHostEnabled,
      change: { isVideoEnabled: !guest.videoSettings.isHostEnabled },
    },
    {
      key: 'live',
      checked: guest.isLive,
      change: { isLive: !guest.isLive },
    },
  ];
}

function save(guest: GuestStarGuest, slotChange: GuestStarSlotChange): void {
  change.mutate({
    sessionId: props.sessionId,
    slotId: guest.slotId,
    change: slotChange,
  }, { onError: notify });
}

function setVolume(guest: GuestStarGuest, typed: string): void {
  if (typed.trim() !== '') {
    save(guest, { volume: Number(typed) });
  }
}

function askRemove(guest: GuestStarGuest): void {
  reinvite.value = false;
  removing.value = guest;
}

function confirmRemove(): void {
  const guest = removing.value;

  if (guest === null) return;

  remove.mutate(
    {
      sessionId: props.sessionId,
      slotId: guest.slotId,
      guestTwitchUserId: guest.userId,
      shouldReinviteGuest: reinvite.value,
    },
    {
      onSettled: () => {
        removing.value = null;
      },
      onError: notify,
    },
  );
}
</script>

<template>
  <PanelCard
    :title="$t('dashboard.settings.guestStar.slots.title')"
    :state="state"
    :empty-text="$t('dashboard.settings.guestStar.slots.empty')"
    data-panel="slots"
  >
    <template v-if="canWrite" #actions>
      <Button size="sm" variant="outline" data-assign-open @click="assigning = true">
        {{ $t("dashboard.settings.guestStar.slots.assign") }}
      </Button>
    </template>
    <RowGroup>
      <SummaryRow
        v-for="guest in guests"
        :key="guest.slotId"
        wrap
        :data-guest="guest.slotId"
        :title="guest.userDisplayName"
        :detail="$t('dashboard.settings.guestStar.slots.slot', { slot: guest.slotId })"
      >
        <template #trailing>
          <label
            v-for="toggle in togglesOf(guest)"
            :key="toggle.key"
            class="flex items-center gap-1.5 text-sm"
          >
            {{ $t(`dashboard.settings.guestStar.slots.${toggle.key}`) }}
            <GatedSwitch
              action="guest-star:write"
              :model-value="toggle.checked"
              v-bind="{ [`data-guest-${toggle.key}`]: guest.slotId }"
              @update:model-value="save(guest, toggle.change)"
            />
          </label>
          <label class="flex items-center gap-1.5 text-sm">
            {{ $t("dashboard.settings.guestStar.slots.volume") }}
            <Input
              v-if="canWrite"
              type="number"
              min="0"
              max="100"
              class="w-20 text-end"
              :data-guest-volume="guest.slotId"
              :model-value="guest.volume"
              @change="setVolume(guest, ($event.target as HTMLInputElement).value)"
            />
            <span v-else class="tabular-nums" :data-guest-volume="guest.slotId">{{ guest.volume }}</span>
          </label>
          <template v-if="canWrite">
            <Button variant="outline" size="sm" :data-guest-move="guest.slotId" @click="moving = guest">
              {{ $t("dashboard.settings.guestStar.slots.move") }}
            </Button>
            <Button variant="ghost" size="sm" :data-guest-remove="guest.slotId" @click="askRemove(guest)">
              {{ $t("dashboard.settings.guestStar.slots.remove") }}
            </Button>
          </template>
        </template>
      </SummaryRow>
    </RowGroup>
  </PanelCard>
  <AssignDialog v-model:open="assigning" :session-id="sessionId" />
  <MoveDialog :session-id="sessionId" :guest="moving" @close="moving = null" />
  <ConfirmDialog
    :open="removing !== null"
    :title="$t('dashboard.settings.guestStar.slots.removeTitle', { name: removing?.userDisplayName ?? '' })"
    :description="$t('dashboard.settings.guestStar.slots.removeDescription')"
    :action="$t('dashboard.settings.guestStar.slots.removeAction')"
    :pending="remove.isPending.value"
    @update:open="removing = null"
    @confirm="confirmRemove"
  >
    <label class="flex items-center gap-2 text-sm">
      <GatedSwitch v-model="reinvite" action="guest-star:write" data-setting="reinvite" />
      {{ $t("dashboard.settings.guestStar.slots.reinvite") }}
    </label>
  </ConfirmDialog>
</template>
