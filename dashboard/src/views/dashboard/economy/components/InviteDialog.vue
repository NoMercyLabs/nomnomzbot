<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Invite another channel into a savings jar, with the role it gets and the caps on what it may put in
// and take out. The other channel has to accept before it counts.
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useInviteJarChannel, type SavingsJar } from '@/api/economy';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

const roles = ['Partner', 'Viewer'] as const;

const props = defineProps<{ jar: SavingsJar | null }>();
const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const invite = useInviteJarChannel(channelId);

const target = ref('');
const role = ref<string>('Partner');
const contributionCap = ref<number | ''>('');
const withdrawalCap = ref<number | ''>('');
const error = ref<string | undefined>(undefined);

const numberOrNull = (value: number | ''): number | null =>
  value === ''
    ? null
    : value;

watch(open, (isOpen: boolean): void => {
  if (!isOpen) return;

  target.value = '';
  role.value = 'Partner';
  contributionCap.value = '';
  withdrawalCap.value = '';
  error.value = undefined;
});

async function submit(): Promise<void> {
  if (props.jar === null) return;

  if (target.value.trim() === '') {
    error.value = t('dashboard.economy.jars.invite.targetRequired');

    return;
  }

  error.value = undefined;

  try {
    await invite.mutateAsync({
      jarId: props.jar.id,
      invite: {
        jarId: props.jar.id,
        invitedBroadcasterId: target.value.trim(),
        role: role.value,
        contributionCapPerStream: numberOrNull(contributionCap.value),
        withdrawalCap: numberOrNull(withdrawalCap.value),
      },
    });
  } catch (failure) {
    error.value = [errorText(failure, t), ...fieldErrorLines(failure)].join(' ');

    return;
  }

  open.value = false;
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    :title="$t('dashboard.economy.jars.invite.title', { name: jar?.name ?? '' })"
    :description="$t('dashboard.economy.jars.invite.description')"
    :action="$t('dashboard.economy.jars.invite.send')"
    :pending="invite.isPending.value"
    :error="error"
    @submit="submit"
  >
    <Field>
      <div class="flex items-center gap-1.5">
        <FieldLabel for="invite-target">{{
          $t('dashboard.economy.jars.invite.target')
        }}</FieldLabel>
        <FieldInfo>{{ $t('dashboard.economy.jars.invite.targetHelp') }}</FieldInfo>
      </div>
      <Input
        id="invite-target"
        v-model="target"
        autocomplete="off"
        :aria-label="$t('dashboard.economy.jars.invite.target')"
      />
    </Field>
    <Field>
      <FieldLabel>{{ $t('dashboard.economy.jars.invite.role') }}</FieldLabel>
      <Select v-model="role">
        <SelectTrigger :aria-label="$t('dashboard.economy.jars.invite.role')" class="w-full">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="item in roles" :key="item" :value="item">
            {{ $t(`dashboard.economy.jars.invite.roles.${item}`) }}
          </SelectItem>
        </SelectContent>
      </Select>
    </Field>
    <div class="grid gap-4 sm:grid-cols-2">
      <Field>
        <div class="flex items-center gap-1.5">
          <FieldLabel for="invite-contribution">{{
            $t('dashboard.economy.jars.invite.contributionCap')
          }}</FieldLabel>
          <FieldInfo>{{ $t('dashboard.economy.jars.invite.capHelp') }}</FieldInfo>
        </div>
        <Input
          id="invite-contribution"
          v-model.number="contributionCap"
          type="number"
          min="1"
          step="1"
          :aria-label="$t('dashboard.economy.jars.invite.contributionCap')"
        />
      </Field>
      <Field>
        <div class="flex items-center gap-1.5">
          <FieldLabel for="invite-withdrawal">{{
            $t('dashboard.economy.jars.invite.withdrawalCap')
          }}</FieldLabel>
          <FieldInfo>{{ $t('dashboard.economy.jars.invite.capHelp') }}</FieldInfo>
        </div>
        <Input
          id="invite-withdrawal"
          v-model.number="withdrawalCap"
          type="number"
          min="1"
          step="1"
          :aria-label="$t('dashboard.economy.jars.invite.withdrawalCap')"
        />
      </Field>
    </div>
  </FormDialog>
</template>
