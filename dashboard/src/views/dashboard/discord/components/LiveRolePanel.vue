<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The "currently live" role: one Discord role the bot puts on one member while the stream is live.
// While the role is applied the server refuses a change of role or member, so those fields lock.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  liveRoleDefaultEnabled,
  useDeleteDiscordLiveRole,
  useDiscordLiveRole,
  useNotifyRoles,
  useSaveDiscordLiveRole,
  type DiscordConnection,
  type DiscordRole,
} from '@/api/discord';
import SettingRow from '@/components/layout/SettingRow.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { Field, FieldLabel } from '@/components/ui/field';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

import PickerError from './PickerError.vue';

const props = defineProps<{
  connection: DiscordConnection;
  guildRoles: DiscordRole[];
  pickerError: Error | null;
}>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const connectionId = (): string => props.connection.id;
const liveQuery = useDiscordLiveRole(channelId, connectionId);
const stored = computed(() => liveQuery.data.value ?? null);
const save = useSaveDiscordLiveRole(channelId, connectionId, () =>
  stored.value !== null);
const remove = useDeleteDiscordLiveRole(channelId, connectionId);

// A role viewers already pick for pings is not also the role worn while live, so it is left out of this list.
const notifyRoles = useNotifyRoles(channelId, connectionId);
const pickableRoles = computed<DiscordRole[]>(() =>
  props.guildRoles.filter((guildRole) =>
    !(notifyRoles.data.value ?? []).some((picked) => picked.discordRoleId === guildRole.id)));

const roleId = ref<string>('');
const enabled = ref<boolean>(liveRoleDefaultEnabled);

// The form follows the stored rule whenever it loads or changes.
watch(
  stored,
  (rule) => {
    roleId.value = rule?.roleId ?? '';
    enabled.value = rule?.enabled ?? liveRoleDefaultEnabled;
  },
  { immediate: true },
);

const writable = computed<boolean>(() => can('discord:config:write'));
const applied = computed<boolean>(() => stored.value?.isCurrentlyApplied ?? false);
const failure = computed<Error | null>(() => save.error.value ?? remove.error.value);
const busy = computed<boolean>(() => save.isPending.value || remove.isPending.value);
const canSave = computed<boolean>(
  () => writable.value && roleId.value !== '' && !busy.value,
);

// The role goes to the streamer's own Discord account, which the server knows from the install; mutate keeps a
// refusal in the mutation error, which the panel shows.
function saveRule(): void {
  save.mutate({
    roleId: roleId.value,
    enabled: enabled.value,
  });
}

function removeRule(): void {
  remove.mutate();
}
</script>

<template>
  <form class="flex flex-col gap-4" data-live-role-panel @submit.prevent="saveRule">
    <StatusBadge v-if="applied" tone="ok" :label="t('dashboard.discord.liveRole.applied')" data-live-applied />

    <Field>
      <FieldLabel>{{ t('dashboard.discord.liveRole.role') }}</FieldLabel>
      <Select v-model="roleId" :disabled="!writable || applied">
        <SelectTrigger data-field="live-role" class="w-full">
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

    <p data-live-owner class="text-sm text-muted-foreground">
      {{ t('dashboard.discord.liveRole.owner') }}
    </p>

    <div class="rounded-[8px] border border-border p-1">
      <SettingRow
        v-model="enabled"
        data-field="live-enabled"
        :title="t('dashboard.discord.liveRole.enabled')"
        :description="applied ? t('dashboard.discord.liveRole.appliedHelp') : t('dashboard.discord.liveRole.enabledHelp')"
        :disabled="!writable"
      />
    </div>

    <p v-if="failure" role="alert" data-live-error class="text-sm text-destructive">
      {{ errorText(failure, t) }}
    </p>

    <div v-if="writable" class="flex items-center justify-end gap-2">
      <Button
        v-if="stored !== null && !applied"
        type="button"
        variant="ghost"
        :disabled="busy"
        data-remove-live-role
        @click="removeRule"
      >
        {{ t('dashboard.discord.liveRole.remove') }}
      </Button>
      <Button type="submit" :disabled="!canSave" data-save-live-role>
        {{ t('dashboard.discord.editor.save') }}
      </Button>
    </div>
  </form>
</template>
