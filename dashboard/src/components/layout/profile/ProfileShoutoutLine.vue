<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// This channel's own shoutout line for one person: shown, edited in a dialog and removed after a confirm.
// A shoutout and a raid from them both use it; without it the bot falls back to the channel default.
import { computed, ref, watch } from 'vue';

import { useChannelAccess } from '@/api/access';
import { useRemoveShoutoutLine, useSetShoutoutLine } from '@/api/shoutouts';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import FormDialog from '@/components/layout/FormDialog.vue';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const props = defineProps<{
  twitchUserId: string;
  name: string;
  line: string | null;
}>();

const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);
const canWrite = computed(() => access.can('moderation:shoutout'));

const setLine = useSetShoutoutLine(channelId);
const removeLine = useRemoveShoutoutLine(channelId);

const editOpen = ref(false);
const removeOpen = ref(false);
const draft = ref('');

const trimmed = computed(() => draft.value.trim());

watch(editOpen, (isOpen: boolean) => {
  if (isOpen) {
    draft.value = props.line ?? '';
  }
});

function save(): void {
  if (trimmed.value.length === 0) return;

  setLine.mutate({
    targetTwitchUserId: props.twitchUserId,
    targetDisplayName: props.name,
    messageTemplate: trimmed.value,
  });

  editOpen.value = false;
}

function remove(): void {
  removeLine.mutate(props.twitchUserId);

  removeOpen.value = false;
}
</script>

<template>
  <div data-slot="profile-shoutout" class="flex items-center gap-3 text-sm">
    <span
      v-if="line"
      data-profile-shoutout-text
      class="min-w-0 flex-1 wrap-anywhere"
    >{{ line }}</span>
    <span v-else class="min-w-0 flex-1 text-muted-foreground">{{ $t('profile.shoutout.channelDefault') }}</span>
    <Button
      data-profile-shoutout-edit
      type="button"
      variant="outline"
      size="sm"
      :disabled="!canWrite"
      :title="canWrite ? undefined : $t('profile.shoutout.noWrite')"
      @click="editOpen = true"
    >
      {{ line ? $t('profile.shoutout.change') : $t('profile.shoutout.add') }}
    </Button>
    <Button
      v-if="line"
      data-profile-shoutout-remove
      type="button"
      variant="destructive"
      size="sm"
      :disabled="!canWrite"
      @click="removeOpen = true"
    >
      {{ $t('profile.shoutout.remove') }}
    </Button>
  </div>
  <FormDialog
    v-model:open="editOpen"
    data-profile-shoutout-dialog
    :title="$t('profile.shoutout.editTitle', { name })"
    :description="$t('profile.shoutout.help')"
    :action="$t('profile.shoutout.save')"
    @submit="save"
  >
    <TemplateField
      v-model="draft"
      data-profile-shoutout-field
      context="eventResponse"
      :maxlength="500"
      :label="$t('profile.shoutout.label')"
      :placeholder="$t('profile.shoutout.placeholder')"
    />
  </FormDialog>
  <ConfirmDialog
    v-model:open="removeOpen"
    :title="$t('profile.shoutout.removeTitle', { name })"
    :description="line ?? undefined"
    :consequences="[$t('profile.shoutout.removeEffect')]"
    :action="$t('profile.shoutout.remove')"
    @confirm="remove"
  />
</template>
