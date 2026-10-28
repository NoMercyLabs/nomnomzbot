<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One self-assign role: which Discord role it wraps, whether viewers may pick it and get a DM,
// and the channel that holds its opt-in button. The button posts only for a stored role.
import type { AcceptableValue } from 'reka-ui';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useCreateNotifyRole,
  useDeleteNotifyRole,
  useDiscordLiveRole,
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

import BlockedChannels from './BlockedChannels.vue';
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
const remove = useDeleteNotifyRole(channelId, () => props.connectionId);
const liveRole = useDiscordLiveRole(channelId, () => props.connectionId);

// The role worn while live is not also a role viewers pick, so it is left out of this list.
const pickableRoles = computed<DiscordRole[]>(() =>
  props.guildRoles.filter((guildRole) => guildRole.id !== liveRole.data.value?.roleId));
const confirmingRemove = ref<boolean>(false);

const discordRoleId = ref<string>(props.role?.discordRoleId ?? '');
const roleName = ref<string>(props.role?.roleName ?? '');
const selfAssignEnabled = ref<boolean>(props.role?.selfAssignEnabled ?? true);
const dmEnabled = ref<boolean>(props.role?.dmEnabled ?? false);
const buttonChannelId = ref<string>(props.role?.buttonChannelId ?? '');

const failure = computed<Error | null>(
  () => create.error.value ?? save.error.value ?? postButton.error.value ?? remove.error.value,
);
const busy = computed<boolean>(
  () => create.isPending.value || save.isPending.value || remove.isPending.value,
);
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

async function removeRole(): Promise<void> {
  if (props.role !== null) {
    await remove.mutateAsync(props.role.id);
    emit('saved');
  }
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
          <SelectItem v-for="guildRole in pickableRoles" :key="guildRole.id" :value="guildRole.id">
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
      <BlockedChannels :channels="channels" />
      <PickerError :error="pickerError" />
    </Field>

    <p v-if="failure" role="alert" data-role-error class="text-sm text-destructive">
      {{ errorText(failure, t) }}
    </p>

    <div v-if="role !== null && confirmingRemove" class="flex flex-col gap-2" data-remove-confirm>
      <p class="text-sm text-muted-foreground">
        {{ t('dashboard.discord.roles.removeConfirm') }}
      </p>
      <div class="flex items-center justify-end gap-2">
        <Button type="button" variant="ghost" :disabled="busy" @click="confirmingRemove = false">
          {{ t('dashboard.discord.roles.keep') }}
        </Button>
        <Button type="button"
                variant="destructive"
                :disabled="busy"
                data-remove-role-yes
                @click="removeRole"
        >
          {{ t('dashboard.discord.roles.removeNow') }}
        </Button>
      </div>
    </div>

    <div v-else class="flex items-center justify-end gap-2">
      <Button
        v-if="role !== null"
        type="button"
        variant="ghost"
        :disabled="busy"
        data-remove-role
        @click="confirmingRemove = true"
      >
        {{ t('dashboard.discord.roles.remove') }}
      </Button>
      <Button type="submit" :disabled="!canSave" data-save-role>
        {{ t('dashboard.discord.editor.save') }}
      </Button>
    </div>
  </form>
</template>
