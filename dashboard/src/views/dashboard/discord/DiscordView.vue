<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Discord: the linked server first, then one notification rule per trigger. "Stream goes live" leads the list.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useDiscordChannels,
  useDiscordConnections,
  useDiscordRoles,
  useDiscordRules,
  useDiscordTriggers,
  useNotifyRoles,
  type NotifyRole,
  type DiscordConnection,
  type DiscordRule,
  type DiscordTrigger,
} from '@/api/discord';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import Cross from '~icons/nnz/cross';
import { Button } from '@/components/ui/button';
import {
  Sheet,
  SheetClose,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { merge } from '@/lib/array';
import { usePanelState } from '@/composables/usePanelState';

import ConnectionPanel from './components/ConnectionPanel.vue';
import LiveRolePanel from './components/LiveRolePanel.vue';
import NotifyRoleEditor from './components/NotifyRoleEditor.vue';
import PostLogPanel from './components/PostLogPanel.vue';
import RuleEditor from './components/RuleEditor.vue';
import SetupGuide from './components/SetupGuide.vue';
import { installErrorKey, readInstallError } from './installError';
import { triggerLabel as labelOf, triggerPlaceholders } from './triggers';
import { useDiscordInstall } from './useDiscordInstall';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const installError = readInstallError();
const { url: installUrl, start: startInstall } = useDiscordInstall(channelId);

const connectionsQuery = useDiscordConnections(channelId);
const connection = computed<DiscordConnection | null>(
  () => connectionsQuery.data.value?.[0] ?? null,
);
const connectionId = computed<string | null>(() => connection.value?.id ?? null);
// Notifications are set up only once the bot is in the server; until then the page leads with adding it.
const botIn = computed<boolean>(() => connection.value?.botInstalled === true);

const rulesQuery = useDiscordRules(channelId, connectionId);
const triggersQuery = useDiscordTriggers(channelId);
const offered = computed<DiscordTrigger[]>(() => triggersQuery.data.value ?? []);
const channelsQuery = useDiscordChannels(channelId, connectionId);
const rolesQuery = useDiscordRoles(channelId, connectionId);
const notifyRolesQuery = useNotifyRoles(channelId, connectionId);
// A failed channel or role read leaves a picker empty; the editors show the bot's reason beside it.
const pickerError = computed<Error | null>(
  () => channelsQuery.error.value ?? rolesQuery.error.value,
);

const connectionState = usePanelState(connectionsQuery);
const rulesState = usePanelState(rulesQuery);
const notifyRolesState = usePanelState(
  notifyRolesQuery,
  (rows: NotifyRole[]): boolean => rows.length === 0,
);
const notifyRoles = computed<NotifyRole[]>(() => notifyRolesQuery.data.value ?? []);
// 'new' opens an empty editor; a role id opens that stored role.
const editingRole = ref<string | null>(null);
const roleOpen = computed<boolean>({
  get: () => editingRole.value !== null,
  set: (value: boolean) => {
    if (!value) {
      editingRole.value = null;
    }
  },
});
const roleBeingEdited = computed<NotifyRole | null>(
  () => notifyRoles.value.find((role) => role.id === editingRole.value) ?? null,
);

const rules = computed<DiscordRule[]>(() => rulesQuery.data.value ?? []);
const editing = ref<string | null>(null);
const open = computed<boolean>({
  get: () => editing.value !== null,
  set: (value: boolean) => {
    if (!value) {
      editing.value = null;
    }
  },
});

// A rule row per trigger the bot posts for, then the rules the bot holds for any other trigger.
const triggers = computed(() => {
  const known = offered.value.map((trigger) => trigger.name);
  const extra = rules.value
    .map((rule) => rule.triggerType)
    .filter((trigger) => !known.includes(trigger));

  return merge(known, extra);
});

function ruleOf(trigger: string): DiscordRule | null {
  return rules.value.find((rule) => rule.triggerType === trigger) ?? null;
}

function triggerLabel(trigger: string): string {
  return labelOf(t, trigger, offered.value);
}

function ruleSummary(rule: DiscordRule | null): string {
  if (rule === null)
    return t('dashboard.discord.rules.notSetUp');

  return rule.enabled
    ? t('dashboard.discord.rules.on')
    : t('dashboard.discord.rules.off');
}
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col">
    <PageHeader :title="t('dashboard.discord.title')" />

    <PageBody>
      <PanelCard
        v-if="installError !== null"
        data-install-error
        tier="lead"
        :title="t('dashboard.discord.installError.title')"
        pad
      >
        <div role="alert" class="flex flex-col gap-3">
          <p class="text-sm">
            {{ t(installErrorKey(installError)) }}
          </p>
          <div v-if="can('discord:connection:write')" class="flex flex-wrap items-center gap-2">
            <Button as-child>
              <a data-install-retry :href="installUrl" @click="startInstall">{{ t('dashboard.discord.installError.retry') }}</a>
            </Button>
            <Button variant="outline" @click="installError = null">
              {{ t('dashboard.discord.installError.dismiss') }}
            </Button>
          </div>
        </div>
      </PanelCard>

      <PanelCard
        v-if="can('discord:connection:read')"
        :title="t('dashboard.discord.connection.title')"
        :state="connectionState"
        :tier="connection ? 'quiet' : 'lead'"
        pad
        :error-text="t('dashboard.discord.connection.error')"
        @retry="connectionsQuery.refetch()"
      >
        <ConnectionPanel v-if="connection" :connection="connection" />
        <SetupGuide v-else-if="connectionsQuery.isSuccess.value" :channel-id="channelId ?? ''" />
      </PanelCard>

      <PanelCard
        v-if="botIn && can('discord:config:read')"
        tier="lead"
        :title="t('dashboard.discord.rules.title')"
        :description="t('dashboard.discord.rules.subtitle')"
        :state="rulesState"
        :error-text="t('dashboard.discord.rules.error')"
        @retry="rulesQuery.refetch()"
      >
        <ul>
          <li v-for="trigger in triggers" :key="trigger" data-rule-row>
            <SummaryRow :title="triggerLabel(trigger)" :detail="ruleSummary(ruleOf(trigger))">
              <template #trailing>
                <Button
                  v-if="can('discord:config:write')"
                  variant="outline"
                  size="sm"
                  :data-edit="trigger"
                  @click="editing = trigger"
                >
                  {{
                    ruleOf(trigger)
                      ? t("dashboard.discord.rules.edit")
                      : t("dashboard.discord.rules.setUp")
                  }}
                </Button>
              </template>
            </SummaryRow>
          </li>
        </ul>
      </PanelCard>

      <PanelCard
        v-if="botIn && can('discord:role:read')"
        :title="t('dashboard.discord.roles.title')"
        :description="t('dashboard.discord.roles.subtitle')"
        :state="notifyRolesState"
        :empty-text="t('dashboard.discord.roles.empty')"
        :error-text="t('dashboard.discord.roles.error')"
        @retry="notifyRolesQuery.refetch()"
      >
        <template #actions>
          <Button
            v-if="can('discord:role:write')"
            variant="outline"
            size="sm"
            data-add-role
            @click="editingRole = 'new'"
          >
            {{ t("dashboard.discord.roles.add") }}
          </Button>
        </template>
        <ul>
          <li v-for="role in notifyRoles" :key="role.id" data-notify-role-row>
            <SummaryRow
              :title="role.roleName ?? role.discordRoleId"
              :detail="t('dashboard.discord.roles.optIns', { count: role.optInCount })"
            >
              <template #trailing>
                <Button
                  v-if="can('discord:role:write')"
                  variant="outline"
                  size="sm"
                  :data-edit-role="role.id"
                  @click="editingRole = role.id"
                >
                  {{ t("dashboard.discord.rules.edit") }}
                </Button>
              </template>
            </SummaryRow>
          </li>
        </ul>
      </PanelCard>

      <PanelCard
        v-if="connection && botIn && can('discord:config:read')"
        :title="t('dashboard.discord.liveRole.title')"
        :description="t('dashboard.discord.liveRole.subtitle')"
        pad
      >
        <LiveRolePanel
          :connection="connection"
          :guild-roles="rolesQuery.data.value ?? []"
          :picker-error="rolesQuery.error.value"
        />
      </PanelCard>

      <PostLogPanel
        v-if="connectionId !== null && can('discord:dispatch:read')"
        :connection-id="connectionId"
      />
    </PageBody>

    <Sheet v-model:open="roleOpen">
      <SheetContent
        v-if="editingRole !== null && connectionId !== null && can('discord:role:write')"
        class="overflow-y-auto"
        :show-close-button="false"
      >
        <SheetClose as-child>
          <Button
            variant="ghost"
            size="icon-sm"
            class="absolute top-3 right-3 z-10"
            :aria-label="t('common.close')"
          >
            <Cross aria-hidden="true" />
          </Button>
        </SheetClose>
        <SheetHeader>
          <SheetTitle>{{
            roleBeingEdited?.roleName ?? t("dashboard.discord.roles.new")
          }}</SheetTitle>
          <SheetDescription>{{ t("dashboard.discord.roles.description") }}</SheetDescription>
        </SheetHeader>
        <div class="p-4">
          <NotifyRoleEditor
            :key="editingRole"
            :role="roleBeingEdited"
            :connection-id="connectionId"
            :guild-roles="rolesQuery.data.value ?? []"
            :channels="channelsQuery.data.value ?? []"
            :picker-error="pickerError"
            @saved="editingRole = null"
          />
        </div>
      </SheetContent>
    </Sheet>

    <Sheet v-model:open="open">
      <SheetContent
        v-if="editing !== null && connectionId !== null && can('discord:config:write')"
        class="overflow-y-auto"
        :show-close-button="false"
      >
        <SheetClose as-child>
          <Button
            variant="ghost"
            size="icon-sm"
            class="absolute top-3 right-3 z-10"
            :aria-label="t('common.close')"
          >
            <Cross aria-hidden="true" />
          </Button>
        </SheetClose>
        <SheetHeader>
          <SheetTitle>{{ triggerLabel(editing) }}</SheetTitle>
          <SheetDescription>{{ t("dashboard.discord.editor.description") }}</SheetDescription>
        </SheetHeader>
        <div class="p-4">
          <RuleEditor
            :key="editing"
            :trigger="editing"
            :placeholders="triggerPlaceholders(editing, offered)"
            :rule="ruleOf(editing)"
            :connection-id="connectionId"
            :channels="channelsQuery.data.value ?? []"
            :roles="rolesQuery.data.value ?? []"
            :picker-error="pickerError"
            @saved="editing = null"
          />
        </div>
      </SheetContent>
    </Sheet>
  </div>
</template>
