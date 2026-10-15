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
  type DiscordConnection,
  type DiscordRule,
} from '@/api/discord';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
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
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { merge } from '@/lib/array';
import { usePanelState } from '@/composables/usePanelState';

import RuleEditor from './components/RuleEditor.vue';
import { discordTriggers } from './triggers';

const { t } = useI18n();
const { channelId } = useCurrentChannel();

const connectionsQuery = useDiscordConnections(channelId);
const connection = computed<DiscordConnection | null>(
  () => connectionsQuery.data.value?.[0] ?? null,
);
const connectionId = computed<string | null>(() => connection.value?.id ?? null);

const rulesQuery = useDiscordRules(channelId, connectionId);
const channelsQuery = useDiscordChannels(channelId, connectionId);
const rolesQuery = useDiscordRoles(channelId, connectionId);

const connectionState = usePanelState(
  connectionsQuery,
  (rows: DiscordConnection[]): boolean => rows.length === 0,
);
const rulesState = usePanelState(rulesQuery);

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

// A rule row per known trigger, then the rules the bot holds for any other trigger.
const triggers = computed(() => {
  const known: string[] = [...discordTriggers];
  const extra = rules.value
    .map((rule: DiscordRule) => rule.triggerType)
    .filter((trigger: string) => !known.includes(trigger));

  return merge(known, extra);
});

function ruleOf(trigger: string): DiscordRule | null {
  return rules.value.find((rule: DiscordRule) => rule.triggerType === trigger) ?? null;
}

function triggerLabel(trigger: string): string {
  // vue-i18n reads a dot as a path step, so the key spells the trigger with underscores.
  const key = `dashboard.discord.triggers.${trigger.replaceAll('.', '_')}`;

  return (discordTriggers as readonly string[]).includes(trigger)
    ? t(key)
    : trigger;
}

const connectionTone = computed<StatusTone>(() => {
  const link = connection.value;

  if (link === null || !link.isLinkActive)
    return 'off';

  return link.botInstalled && link.serverConsentStatus === 'approved'
    ? 'ok'
    : 'bad';
});

const connectionLabel = computed<string>(() => {
  const link = connection.value;

  if (link === null)
    return t('dashboard.discord.connection.none');

  if (!link.botInstalled)
    return t('dashboard.discord.connection.botMissing');

  return link.serverConsentStatus === 'approved'
    ? t('dashboard.discord.connection.ready')
    : t('dashboard.discord.connection.consentPending');
});

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

    <div class="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto p-6">
      <PanelCard
        :title="t('dashboard.discord.connection.title')"
        :state="connectionState"
        pad
        :empty-text="t('dashboard.discord.connection.empty')"
        :error-text="t('dashboard.discord.connection.error')"
        @retry="connectionsQuery.refetch()"
      >
        <div data-connection class="flex flex-col gap-1">
          <p class="text-sm font-medium">
            {{ connection?.guildName ?? connection?.guildId }}
          </p>
          <StatusBadge :tone="connectionTone" :label="connectionLabel" />
        </div>
      </PanelCard>

      <PanelCard
        v-if="connection"
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
                  variant="outline"
                  size="sm"
                  :data-edit="trigger"
                  @click="editing = trigger"
                >
                  {{ ruleOf(trigger) ? t('dashboard.discord.rules.edit') : t('dashboard.discord.rules.setUp') }}
                </Button>
              </template>
            </SummaryRow>
          </li>
        </ul>
      </PanelCard>
    </div>

    <Sheet v-model:open="open">
      <SheetContent
        v-if="editing !== null && connectionId !== null"
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
          <SheetDescription>{{ t('dashboard.discord.editor.description') }}</SheetDescription>
        </SheetHeader>
        <div class="p-4">
          <RuleEditor
            :key="editing"
            :trigger="editing"
            :rule="ruleOf(editing)"
            :connection-id="connectionId"
            :channels="channelsQuery.data.value ?? []"
            :roles="rolesQuery.data.value ?? []"
            @saved="editing = null"
          />
        </div>
      </SheetContent>
    </Sheet>
  </div>
</template>
