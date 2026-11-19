<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Invite a person to the running session by their Twitch user id.
import { computed, ref, watch } from 'vue';

import { useInviteGuestStarGuest } from '@/api/guestStar';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { useGuestStarFailure } from '../guestStarFailure';

const props = defineProps<{ sessionId: string }>();
const open = defineModel<boolean>('open', { required: true });

const { reasonText } = useGuestStarFailure();
const { channelId } = useCurrentChannel();

const invite = useInviteGuestStarGuest(channelId);
const userId = ref('');
const error = computed(() =>
  invite.error.value === null
    ? undefined
    : reasonText(invite.error.value));

watch(open, (isOpen) => {
  if (!isOpen) return;

  userId.value = '';
  invite.reset();
});

function submit(): void {
  invite.mutate(
    { sessionId: props.sessionId, guestTwitchUserId: userId.value.trim() },
    { onSuccess: () => {
      open.value = false;
    } },
  );
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    :title="$t('dashboard.settings.guestStar.invites.dialogTitle')"
    :description="$t('dashboard.settings.guestStar.invites.dialogDescription')"
    :action="$t('dashboard.settings.guestStar.invites.send')"
    :pending="invite.isPending.value"
    :disabled="userId.trim() === ''"
    :error="error"
    @submit="submit"
  >
    <Field>
      <FieldLabel for="guest-star-invite-user">{{ $t("dashboard.settings.guestStar.invites.userId") }}</FieldLabel>
      <Input
        id="guest-star-invite-user"
        v-model="userId"
        data-field="guestTwitchUserId"
        inputmode="numeric"
        autocomplete="off"
        :placeholder="$t('dashboard.settings.guestStar.invites.userIdPlaceholder')"
      />
    </Field>
  </FormDialog>
</template>
