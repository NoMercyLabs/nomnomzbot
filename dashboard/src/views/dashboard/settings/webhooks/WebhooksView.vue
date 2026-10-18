<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Webhooks: the outbound ones post the channel's events to an outside address; the inbound ones are links an
// outside provider posts to. Both lists follow the bot: a change made anywhere shows without a reload.
import Add from '~icons/nnz/add';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  fetchInboundWebhookBlastRadius,
  fetchOutboundWebhookBlastRadius,
  useDeleteInboundWebhook,
  useDeleteOutboundWebhook,
  useInboundWebhooks,
  useOutboundWebhooks,
  useReenableOutboundWebhook,
  useSaveOutboundWebhook,
  useSetInboundWebhookEnabled,
  useWebhookEventCatalog,
  type InboundWebhook,
  type OutboundWebhook,
  type OutboundWebhookDraft,
  type WebhookBlastRadius,
} from '@/api/webhooks';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { merge } from '@/lib/array';
import { errorText, fieldErrorLines } from '@/lib/errors';

import InboundWebhookRow from './components/InboundWebhookRow.vue';
import OutboundWebhookDialog from './components/OutboundWebhookDialog.vue';
import OutboundWebhookRow from './components/OutboundWebhookRow.vue';

const { t, te } = useI18n();
const { channelId } = useCurrentChannel();
const outbound = useOutboundWebhooks(channelId);
const inbound = useInboundWebhooks(channelId);
const catalog = useWebhookEventCatalog(channelId);
const save = useSaveOutboundWebhook(channelId);
const removeOutbound = useDeleteOutboundWebhook(channelId);
const removeInbound = useDeleteInboundWebhook(channelId);
const reenable = useReenableOutboundWebhook(channelId);
const setInbound = useSetInboundWebhookEnabled(channelId);

const outboundState = usePanelState(outbound, (rows) =>
  rows.length === 0);

const inboundState = usePanelState(inbound, (rows) =>
  rows.length === 0);

const outboundRows = computed(() => outbound.data.value ?? []);
const inboundRows = computed(() => inbound.data.value ?? []);

const editorOpen = ref(false);
const editing = ref<OutboundWebhook | null>(null);
const saveError = ref<string | undefined>(undefined);
const pageError = ref<string | undefined>(undefined);
const busy = ref(false);

const confirmOpen = ref(false);
const deletingOutbound = ref<OutboundWebhook | null>(null);
const deletingInbound = ref<InboundWebhook | null>(null);
const consequences = ref<string[]>([]);

function openEditor(endpoint: OutboundWebhook | null): void {
  editing.value = endpoint;
  saveError.value = undefined;
  editorOpen.value = true;
}

async function submit(draft: OutboundWebhookDraft): Promise<void> {
  saveError.value = undefined;

  try {
    await save.mutateAsync({ id: editing.value?.id ?? null, draft });
    editorOpen.value = false;
  } catch (failure) {
    saveError.value = [errorText(failure, t), ...fieldErrorLines(failure)].join(' ');
  }
}

// One page action at a time: the switch, the re-enable and the delete all report a refusal in the same place.
async function run(action: () => Promise<void>): Promise<void> {
  if (busy.value) return;

  pageError.value = undefined;
  busy.value = true;

  try {
    await action();
  } catch (failure) {
    pageError.value = errorText(failure, t);
  } finally {
    busy.value = false;
  }
}

function flipOutbound(endpoint: OutboundWebhook): Promise<void> {
  const draft: OutboundWebhookDraft = {
    name: endpoint.name,
    fqdn: endpoint.fqdn,
    path: endpoint.path ?? null,
    subscribedEventTypes: endpoint.subscribedEventTypes,
    isEnabled: !endpoint.isEnabled,
  };

  return run(() =>
    save.mutateAsync({ id: endpoint.id, draft }));
}

function flipInbound(endpoint: InboundWebhook): Promise<void> {
  return run(() =>
    setInbound.mutateAsync({ id: endpoint.id, isEnabled: !endpoint.isEnabled }));
}

function categoryLabel(key: string): string {
  const path = `dashboard.settings.webhooks.delete.category.${key}`;

  return te(path)
    ? t(path)
    : key.replaceAll('_', ' ');
}

function consequenceLines(blast: WebhookBlastRadius): readonly string[] {
  const counted = blast.categories.filter((category) => Number(category.count) > 0);

  const lines = counted.map((category) =>
    t('dashboard.settings.webhooks.delete.line', {
      count: Number(category.count),
      what: categoryLabel(category.categoryKey),
    }));

  if (lines.length === 0)
    return [t('dashboard.settings.webhooks.delete.nothing')];

  return blast.isMinimum
    ? merge(lines, [t('dashboard.settings.webhooks.delete.minimum')])
    : lines;
}

// The blast radius is counted by the bot before the confirm opens: an endpoint whose reach cannot be read is not offered for delete.
async function askDeleteInbound(endpoint: InboundWebhook): Promise<void> {
  await run(async () => {
    const blast = await fetchInboundWebhookBlastRadius(channelId.value as string, endpoint.id);

    consequences.value = [...consequenceLines(blast)];
    deletingOutbound.value = null;
    deletingInbound.value = endpoint;
    confirmOpen.value = true;
  });
}

async function askDeleteOutbound(endpoint: OutboundWebhook): Promise<void> {
  await run(async () => {
    const blast = await fetchOutboundWebhookBlastRadius(channelId.value as string, endpoint.id);

    const stops = [t('dashboard.settings.webhooks.delete.outboundStops')];

    consequences.value = [...merge(stops, consequenceLines(blast))];
    deletingInbound.value = null;
    deletingOutbound.value = endpoint;
    confirmOpen.value = true;
  });
}

async function confirmDelete(): Promise<void> {
  const outboundTarget = deletingOutbound.value;
  const inboundTarget = deletingInbound.value;

  await run(async () => {
    if (outboundTarget !== null) {
      await removeOutbound.mutateAsync(outboundTarget.id);
    }

    if (inboundTarget !== null) {
      await removeInbound.mutateAsync(inboundTarget.id);
    }
  });

  confirmOpen.value = false;
}
</script>

<template>
  <PageHeader :title="$t('frame.nav.settingsPages.webhooks')" :crumb="$t('dashboard.settings.crumb')" />

  <PageBody>
    <PanelCard
      data-view="webhooks-outbound"
      tier="lead"
      :title="$t('dashboard.settings.webhooks.outbound.title')"
      :description="$t('dashboard.settings.webhooks.outbound.description')"
      :state="outboundState"
      :empty-text="$t('dashboard.settings.webhooks.outbound.empty')"
      :error-text="$t('dashboard.settings.webhooks.outbound.loadFailed')"
      @retry="outbound.refetch()"
    >
      <template #actions>
        <Button data-new-outbound @click="openEditor(null)">
          <Add class="size-5" />
          {{ $t('dashboard.settings.webhooks.outbound.new') }}
        </Button>
      </template>
      <RowGroup>
        <OutboundWebhookRow
          v-for="endpoint in outboundRows"
          :key="endpoint.id"
          :endpoint="endpoint"
          :busy="busy"
          @toggle="flipOutbound(endpoint)"
          @reenable="run(() => reenable.mutateAsync(endpoint.id))"
          @edit="openEditor(endpoint)"
          @remove="askDeleteOutbound(endpoint)"
        />
      </RowGroup>
    </PanelCard>
    <PanelCard
      data-view="webhooks-inbound"
      :title="$t('dashboard.settings.webhooks.inbound.title')"
      :description="$t('dashboard.settings.webhooks.inbound.description')"
      :state="inboundState"
      :empty-text="$t('dashboard.settings.webhooks.inbound.empty')"
      :error-text="$t('dashboard.settings.webhooks.inbound.loadFailed')"
      @retry="inbound.refetch()"
    >
      <RowGroup>
        <InboundWebhookRow
          v-for="endpoint in inboundRows"
          :key="endpoint.id"
          :endpoint="endpoint"
          :busy="busy"
          @toggle="flipInbound(endpoint)"
          @remove="askDeleteInbound(endpoint)"
        />
      </RowGroup>
    </PanelCard>
    <p v-if="pageError" role="alert" class="pt-3 text-sm text-destructive">{{ pageError }}</p>
  </PageBody>

  <OutboundWebhookDialog
    v-model:open="editorOpen"
    :endpoint="editing"
    :catalog="catalog.data.value ?? []"
    :pending="save.isPending.value"
    :error="saveError"
    @submit="submit"
  />
  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="$t('dashboard.settings.webhooks.delete.title', { name: deletingOutbound?.name ?? deletingInbound?.name ?? '' })"
    :description="$t('dashboard.settings.webhooks.delete.description')"
    :consequences="consequences"
    :action="$t('dashboard.settings.webhooks.delete.action')"
    :pending="busy"
    @confirm="confirmDelete"
  />
</template>
