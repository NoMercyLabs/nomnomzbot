<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Put a ready guest (by Twitch user id) into a slot of the running session.
import { computed, ref, watch } from 'vue';

import { useAssignGuestStarSlot } from '@/api/guestStar';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { useGuestStarFailure } from '../guestStarFailure';

const props = defineProps<{ sessionId: string }>();
const open = defineModel<boolean>('open', { required: true });

const { reasonText } = useGuestStarFailure();
const { channelId } = useCurrentChannel();

const assign = useAssignGuestStarSlot(channelId);
const userId = ref('');
const slotId = ref('');
const error = computed(() =>
  assign.error.value === null
    ? undefined
    : reasonText(assign.error.value));

watch(open, (isOpen) => {
  if (!isOpen) return;

  userId.value = '';
  slotId.value = '';
  assign.reset();
});

function submit(): void {
  assign.mutate(
    {
      sessionId: props.sessionId,
      guestTwitchUserId: userId.value.trim(),
      slotId: slotId.value.trim(),
    },
    { onSuccess: () => {
      open.value = false;
    } },
  );
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    :title="$t('dashboard.settings.guestStar.slots.assignTitle')"
    :description="$t('dashboard.settings.guestStar.slots.assignDescription')"
    :action="$t('dashboard.settings.guestStar.slots.assignAction')"
    :pending="assign.isPending.value"
    :disabled="userId.trim() === '' || slotId.trim() === ''"
    :error="error"
    @submit="submit"
  >
    <Field>
      <FieldLabel for="guest-star-assign-user">{{ $t("dashboard.settings.guestStar.slots.userId") }}</FieldLabel>
      <Input
        id="guest-star-assign-user"
        v-model="userId"
        data-field="guestTwitchUserId"
        inputmode="numeric"
        autocomplete="off"
      />
    </Field>
    <Field>
      <FieldLabel for="guest-star-assign-slot">{{ $t("dashboard.settings.guestStar.slots.slotId") }}</FieldLabel>
      <Input
        id="guest-star-assign-slot"
        v-model="slotId"
        data-field="slotId"
        inputmode="numeric"
        autocomplete="off"
        :placeholder="$t('dashboard.settings.guestStar.slots.slotPlaceholder')"
      />
    </Field>
  </FormDialog>
</template>
