<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Move the guest of one slot to another slot. No destination moves the guest to the empty interface.
import { computed, ref, watch } from 'vue';

import { useMoveGuestStarSlot, type GuestStarGuest } from '@/api/guestStar';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { useGuestStarFailure } from '../guestStarFailure';

const props = defineProps<{ sessionId: string; guest: GuestStarGuest | null }>();
const emit = defineEmits<{ close: [] }>();

const { reasonText } = useGuestStarFailure();
const { channelId } = useCurrentChannel();

const move = useMoveGuestStarSlot(channelId);
const destination = ref('');
const open = computed(() => props.guest !== null);
const error = computed(() =>
  move.error.value === null
    ? undefined
    : reasonText(move.error.value));

watch(() => props.guest, () => {
  destination.value = '';
  move.reset();
});

function submit(): void {
  if (props.guest === null) return;

  const target = destination.value.trim();

  move.mutate(
    {
      sessionId: props.sessionId,
      slotId: props.guest.slotId,
      destinationSlotId: target === ''
        ? null
        : target,
    },
    { onSuccess: () => emit('close') },
  );
}
</script>

<template>
  <FormDialog
    :open="open"
    :title="$t('dashboard.settings.guestStar.slots.moveTitle', { name: guest?.userDisplayName ?? '' })"
    :description="$t('dashboard.settings.guestStar.slots.moveDescription')"
    :action="$t('dashboard.settings.guestStar.slots.moveAction')"
    :pending="move.isPending.value"
    :error="error"
    @update:open="emit('close')"
    @submit="submit"
  >
    <Field>
      <FieldLabel for="guest-star-move-slot">{{ $t("dashboard.settings.guestStar.slots.destination") }}</FieldLabel>
      <Input
        id="guest-star-move-slot"
        v-model="destination"
        data-field="destinationSlotId"
        inputmode="numeric"
        autocomplete="off"
        :placeholder="$t('dashboard.settings.guestStar.slots.slotPlaceholder')"
      />
    </Field>
  </FormDialog>
</template>
