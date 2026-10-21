<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One self-assign role: which Discord role it wraps, whether viewers may pick it and get a DM,
// and the channel that holds its opt-in button. The button posts only for a stored role.
import type { AcceptableValue } from 'reka-ui';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useCreateNotifyRole,
  usePostRoleButton,
  useSaveNotifyRole,
  type DiscordChannel,
  type DiscordRole,
  type NotifyRole,
} from '@/api/discord';
import SettingRow from '@/components/layout/SettingRow.vue';
import { Button } from '@/components/ui/button';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

import PickerError from './PickerError.vue';

const props = defineProps<{
  role: NotifyRole | null;
  connectionId: string;
  guildRoles: DiscordRole[];
  channels: DiscordChannel[];
  pickerError: Error | null;
}>();

const emit = defineEmits<{ saved: [] }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const create = useCreateNotifyRole(channelId, () => props.connectionId);
const save = useSaveNotifyRole(channelId, () => props.connectionId);
const postButton = usePostRoleButton(channelId, () => props.connectionId);

const discordRoleId = ref<string>(props.role?.discordRoleId ?? '');
const roleName = ref<string>(props.role?.roleName ?? '');
const selfAssignEnabled = ref<boolean>(props.role?.selfAssignEnabled ?? true);
const dmEnabled = ref<boolean>(props.role?.dmEnabled ?? false);
const buttonChannelId = ref<string>(props.role?.buttonChannelId ?? '');

const failure = computed<Error | null>(
  () => create.error.value ?? save.error.value ?? postButton.error.value,
);
const busy = computed<boolean>(() => create.isPending.value || save.isPending.value);
const canSave = computed<boolean>(() => discordRoleId.value !== '' && !busy.value);
const canPost = computed<boolean>(
  () => props.role !== null && buttonChannelId.value !== '' && !postButton.isPending.value,
);

function blankToNull(value: string): string | null {
  return value.trim() === ''
    ? null
    : value.trim();
}

// Picking a Discord role fills the display name, which the streamer may then change.
function pickRole(id: AcceptableValue): void {
  discordRoleId.value = String(id);

  if (roleName.value === '') {
    roleName.value = props.guildRoles.find((role) => role.id === id)?.name ?? '';
  }
}

async function saveRole(): Promise<void> {
  const change = {
    roleName: blankToNull(roleName.value),
    selfAssignEnabled: selfAssignEnabled.value,
    dmEnabled: dmEnabled.value,
  };

  if (props.role === null) {
    await create.mutateAsync({ ...change, discordRoleId: discordRoleId.value });
  } else {
    await save.mutateAsync({ roleId: props.role.id, change });
  }

  emit('saved');
}

async function postRoleButton(): Promise<void> {
  if (props.role !== null) {
    await postButton.mutateAsync({ roleId: props.role.id, buttonChannelId: buttonChannelId.value });
  }
}
</script>

<template>
  <form class="flex flex-col gap-5" data-notify-role-editor @submit.prevent="saveRole">
    <Field>
      <FieldLabel>{{ t('dashboard.discord.roles.discordRole') }}</FieldLabel>
      <Select
        :model-value="discordRoleId"
        :disabled="role !== null"
        @update:model-value="pickRole"
      >
        <SelectTrigger data-field="discord-role" class="w-full">
          <SelectValue :placeholder="t('dashboard.discord.roles.discordRolePlaceholder')" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="guildRole in guildRoles" :key="guildRole.id" :value="guildRole.id">
            {{ t('dashboard.discord.editor.roleOption', { name: guildRole.name }) }}
          </SelectItem>
        </SelectContent>
      </Select>
      <PickerError :error="pickerError" />
    </Field>

    <Field>
      <FieldLabel>{{ t('dashboard.discord.roles.name') }}</FieldLabel>
      <Input v-model="roleName" data-field="role-name" />
    </Field>

    <div class="rounded-[8px] border border-border p-1">
      <SettingRow
        v-model="selfAssignEnabled"
        data-field="self-assign"
        :title="t('dashboard.discord.roles.selfAssign')"
        :description="t('dashboard.discord.roles.selfAssignHelp')"
      />
      <SettingRow
        v-model="dmEnabled"
        data-field="dm-enabled"
        :title="t('dashboard.discord.roles.dm')"
        :description="t('dashboard.discord.roles.dmHelp')"
      />
    </div>

    <Field v-if="role !== null">
      <FieldLabel>{{ t('dashboard.discord.roles.buttonChannel') }}</FieldLabel>
      <div class="flex items-center gap-2">
        <Select v-model="buttonChannelId">
          <SelectTrigger data-field="button-channel" class="w-full">
            <SelectValue :placeholder="t('dashboard.discord.editor.channelPlaceholder')" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem
              v-for="channel in channels"
              :key="channel.id"
              :value="channel.id"
              :disabled="!channel.canPost"
            >
              {{ t('dashboard.discord.editor.channelOption', { name: channel.name }) }}
            </SelectItem>
          </SelectContent>
        </Select>
        <Button
          type="button"
          variant="outline"
          :disabled="!canPost"
          data-post-button
          @click="postRoleButton"
        >
          {{ t('dashboard.discord.roles.postButton') }}
        </Button>
      </div>
      <PickerError :error="pickerError" />
    </Field>

    <p v-if="failure" role="alert" data-role-error class="text-sm text-destructive">
      {{ errorText(failure, t) }}
    </p>

    <div class="flex items-center justify-end gap-2">
      <Button type="submit" :disabled="!canSave" data-save-role>
        {{ t('dashboard.discord.editor.save') }}
      </Button>
    </div>
  </form>
</template>
