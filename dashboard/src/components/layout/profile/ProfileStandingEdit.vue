<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Picks the bot standing for one person. Every choice lists what it does, so the moderator sees the effect
// before saving. The change shows at once; a refused save rolls back and reaches the error inbox.
import { ref, watch } from 'vue';

import { STANDING_LEVELS, useSetStanding, type StandingLevel } from '@/api/moderation';
import FormDialog from '@/components/layout/FormDialog.vue';
import {
  Field,
  FieldContent,
  FieldInfo,
  FieldLabel,
  FieldTitle,
} from '@/components/ui/field';
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group';
import { Textarea } from '@/components/ui/textarea';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { merge } from '@/lib/array';

const REASON_MAX = 500;
const CHOICES = merge(['normal'] as const, STANDING_LEVELS);

type Choice = (typeof CHOICES)[number];

const props = defineProps<{
  userId: string;
  name: string;
  level: StandingLevel | null;
  savedReason: string | null;
}>();
const open = defineModel<boolean>('open', { required: true });

const { channelId } = useCurrentChannel();
const setStanding = useSetStanding(channelId);

const choice = ref<Choice>('normal');
const reason = ref('');

watch(open, (isOpen: boolean) => {
  if (!isOpen) return;

  choice.value = props.level ?? 'normal';
  reason.value = props.savedReason ?? '';
});

function save(): void {
  const level = choice.value === 'normal'
    ? null
    : choice.value;
  const trimmed = reason.value.trim();

  setStanding.mutate({
    userId: props.userId,
    level,
    reason: trimmed.length > 0
      ? trimmed
      : null,
  });

  open.value = false;
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-profile-standing-dialog
    :title="$t('profile.botStanding.editTitle', { name })"
    :description="$t('profile.botStanding.editDescription')"
    :action="$t('profile.botStanding.save')"
    @submit="save"
  >
    <RadioGroup v-model="choice" data-profile-standing-choices class="gap-2">
      <FieldLabel v-for="option in CHOICES" :key="option" :for="`profile-standing-${option}`">
        <Field orientation="horizontal">
          <FieldContent>
            <div class="flex items-center gap-1.5">
              <FieldTitle>{{ $t(`profile.botStanding.levels.${option}.name`) }}</FieldTitle>
              <FieldInfo>{{ $t(`profile.botStanding.levels.${option}.effect`) }}</FieldInfo>
            </div>
          </FieldContent>
          <RadioGroupItem
            :id="`profile-standing-${option}`"
            :value="option"
            :data-profile-standing-choice="option"
          />
        </Field>
      </FieldLabel>
    </RadioGroup>
    <Field v-if="choice !== 'normal'">
      <FieldLabel for="profile-standing-reason">{{ $t('profile.botStanding.reason') }}</FieldLabel>
      <Textarea
        id="profile-standing-reason"
        v-model="reason"
        data-profile-standing-reason
        rows="2"
        :maxlength="REASON_MAX"
        :placeholder="$t('profile.botStanding.reasonHint')"
        @keydown.enter.ctrl.prevent="save"
      />
    </Field>
  </FormDialog>
</template>
