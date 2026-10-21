<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The team: who manages the channel and with which role. Roles are named, never numbered.
import Add from '~icons/nnz/add';
import Trash from '~icons/nnz/trash';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import type { ViewerOption } from '@/api/community';
import {
  teamRoleOf,
  teamRoles,
  useMembers,
  useRemoveMember,
  useSetMemberRole,
  type TeamRole,
} from '@/api/permissions';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { errorText } from '@/lib/errors';

import OptionSelect, { type Option } from './OptionSelect.vue';
import PersonPicker from './PersonPicker.vue';

defineProps<{ canManage: boolean }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const members = useMembers(channelId);
const setRole = useSetMemberRole(channelId);
const remove = useRemoveMember(channelId);
const state = usePanelState(members);

const rows = computed(() => members.data.value ?? []);
const picked = ref<ViewerOption | null>(null);
const newRole = ref<TeamRole>('Moderator');
const error = ref<string | undefined>(undefined);

const roleLabel = (role: string): string => t(`dashboard.settings.permissions.role.${role}`);

const grantable = teamRoles.filter((role) => role !== 'Broadcaster');
const toOption = (role: TeamRole): Option => ({ value: role, label: roleLabel(role) });
const addOptions = computed<Option[]>(() => grantable.map(toOption));

const rowOptions = computed<Option[]>(() =>
  teamRoles.map((role) => ({
    value: role,
    label: roleLabel(role),
    disabled: role === 'Broadcaster',
  })));

async function run(action: Promise<void>): Promise<void> {
  error.value = undefined;

  try {
    await action;
  } catch (failure) {
    error.value = errorText(failure, t);
  }
}

const changeRole = (userId: string, role: TeamRole): Promise<void> =>
  run(setRole.mutateAsync({ userId, role }));

async function add(): Promise<void> {
  if (picked.value === null) return;

  await run(setRole.mutateAsync({ userId: picked.value.id, role: newRole.value }));
  picked.value = null;
}
</script>

<template>
  <PanelCard
    data-panel="team-roles"
    tier="lead"
    :title="$t('dashboard.settings.permissions.team.title')"
    :description="$t('dashboard.settings.permissions.team.description')"
    :state="state"
    :error-text="$t('dashboard.settings.permissions.team.loadFailed')"
    @retry="members.refetch()"
  >
    <RowGroup>
      <SummaryRow
        v-for="member in rows"
        wrap
        :key="member.id"
        :data-member="member.username ?? member.userId"
        :title="member.username ?? member.userId"
        :detail="roleLabel(teamRoleOf(member.role))"
      >
        <template #trailing>
          <OptionSelect
            data-member-role
            :label="$t('dashboard.settings.permissions.team.roleOf', { name: member.username ?? member.userId })"
            :model-value="teamRoleOf(member.role)"
            :options="rowOptions"
            :disabled="!canManage || member.isOwner"
            @update:model-value="changeRole(member.userId, $event as TeamRole)"
          />
          <Button
            v-if="canManage"
            variant="ghost"
            size="icon"
            data-member-remove
            :disabled="member.isOwner"
            :aria-label="$t('dashboard.settings.permissions.team.remove', { name: member.username ?? member.userId })"
            @click="run(remove.mutateAsync(member.userId))"
          >
            <Trash class="size-5" />
          </Button>
        </template>
      </SummaryRow>
    </RowGroup>
    <div v-if="canManage" class="flex flex-wrap items-center gap-2 pt-3">
      <PersonPicker :picked="picked" :label="$t('dashboard.settings.permissions.team.person')" @pick="picked = $event" />
      <OptionSelect
        v-model="newRole"
        data-new-role
        :label="$t('dashboard.settings.permissions.team.newRole')"
        :options="addOptions"
      />
      <Button data-member-add :disabled="picked === null || setRole.isPending.value" @click="add">
        <Add class="size-5" />
        {{ $t('dashboard.settings.permissions.team.add') }}
      </Button>
    </div>
    <p v-if="error" role="alert" class="pt-3 text-sm text-destructive">{{ error }}</p>
  </PanelCard>
</template>
