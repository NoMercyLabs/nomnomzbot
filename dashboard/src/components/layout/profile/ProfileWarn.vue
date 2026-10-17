<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Twitch Warn Chat User: the person must accept the warning before they can chat again. Twitch needs a reason
// of at most 500 characters, so the dialog asks for one and counts down to the limit.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { useWarn } from '@/api/people';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldError, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Textarea } from '@/components/ui/textarea';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const REASON_MAX = 500;

const props = defineProps<{ userId: string; name: string }>();
const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const warn = useWarn(channelId);

const reason = ref('');
const tried = ref(false);
const serverError = ref<string | undefined>(undefined);

const trimmed = computed(() => reason.value.trim());
const reasonError = computed(() => {
  if (!tried.value || trimmed.value.length > 0)
    return null;

  return t('profile.warn.reasonNeeded');
});

watch(open, (isOpen: boolean) => {
  if (!isOpen) return;

  reason.value = '';
  tried.value = false;
  serverError.value = undefined;
});

async function send(): Promise<void> {
  tried.value = true;

  if (trimmed.value.length === 0) return;

  serverError.value = undefined;

  try {
    await warn.mutateAsync({ userId: props.userId, reason: trimmed.value });
    toast.success(t('profile.warn.done', { name: props.name }));
    open.value = false;
  } catch {
    serverError.value = t('profile.warn.failed', { name: props.name });
  }
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-profile-warn-dialog
    :title="$t('profile.warn.title', { name })"
    :description="$t('profile.warn.description')"
    :action="$t('profile.warn.action')"
    :pending="warn.isPending.value"
    :error="serverError"
    @submit="send"
  >
    <Field :data-invalid="reasonError ? true : undefined">
      <div class="flex items-center gap-1.5">
        <FieldLabel for="profile-warn-reason">{{ $t('profile.warn.reason') }}</FieldLabel>
        <FieldInfo>{{ $t('profile.warn.left', { count: REASON_MAX - reason.length }) }}</FieldInfo>
      </div>
      <Textarea
        id="profile-warn-reason"
        v-model="reason"
        data-profile-warn-reason
        rows="3"
        :maxlength="REASON_MAX"
        :aria-invalid="reasonError ? true : undefined"
        @keydown.enter.ctrl.prevent="send"
      />
      <FieldError v-if="reasonError">{{ reasonError }}</FieldError>
    </Field>
  </FormDialog>
</template>
