<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
  useRotateInboundWebhookLink,
  useRotateOutboundWebhookSecret,
  useSaveInboundWebhook,
  useSaveOutboundWebhook,
  useSetInboundWebhookEnabled,
  useTestOutboundWebhook,
  useWebhookEventCatalog,
  type InboundWebhook,
  type InboundWebhookDraft,
  type OutboundWebhook,
  type OutboundWebhookDraft,
  type WebhookBlastRadius,
  type WebhookTestResult,
} from '@/api/webhooks';
import { usePipelines } from '@/api/pipelines';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelBeside from '@/components/layout/PanelBeside.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { merge } from '@/lib/array';
import { errorText, fieldErrorLines } from '@/lib/errors';

import EgressAllowlistCard from './components/EgressAllowlistCard.vue';
import InboundWebhookDialog from './components/InboundWebhookDialog.vue';
import InboundWebhookRow from './components/InboundWebhookRow.vue';
import OutboundDeliveriesPanel from './components/OutboundDeliveriesPanel.vue';
import OutboundWebhookDialog from './components/OutboundWebhookDialog.vue';
import OutboundWebhookRow from './components/OutboundWebhookRow.vue';
import SigningSecretDialog from './components/SigningSecretDialog.vue';

const { t, te } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const outbound = useOutboundWebhooks(channelId);
const inbound = useInboundWebhooks(channelId);
const catalog = useWebhookEventCatalog(channelId);
const save = useSaveOutboundWebhook(channelId);
const removeOutbound = useDeleteOutboundWebhook(channelId);
const removeInbound = useDeleteInboundWebhook(channelId);
const reenable = useReenableOutboundWebhook(channelId);
const setInbound = useSetInboundWebhookEnabled(channelId);
const saveInbound = useSaveInboundWebhook(channelId);
const rotateInbound = useRotateInboundWebhookLink(channelId);
const rotateOutbound = useRotateOutboundWebhookSecret(channelId);
const testOutbound = useTestOutboundWebhook(channelId);
const pipelines = usePipelines(channelId);

const outboundState = usePanelState(outbound, (rows) =>
  rows.length === 0);

const inboundState = usePanelState(inbound, (rows) =>
  rows.length === 0);

const outboundRows = computed(() => outbound.data.value ?? []);
const inboundRows = computed(() => inbound.data.value ?? []);

const editorOpen = ref(false);
const editing = ref<OutboundWebhook | null>(null);
const saveError = ref<string | undefined>(undefined);
const revealedSecret = ref<string | null>(null);
const pageError = ref<string | undefined>(undefined);
const busy = ref(false);

const confirmOpen = ref(false);
const deletingOutbound = ref<OutboundWebhook | null>(null);
const deletingInbound = ref<InboundWebhook | null>(null);
const consequences = ref<string[]>([]);

const inboundEditorOpen = ref(false);
const editingInbound = ref<InboundWebhook | null>(null);
const inboundSaveError = ref<string | undefined>(undefined);

const rotateOpen = ref(false);
const rotatingInbound = ref<InboundWebhook | null>(null);
const rotatingOutbound = ref<OutboundWebhook | null>(null);

const testResult = ref<{ name: string; outcome: WebhookTestResult } | null>(null);

const deliveriesOpen = ref(false);
const deliveriesFor = ref<OutboundWebhook | null>(null);

function openInboundEditor(endpoint: InboundWebhook | null): void {
  editingInbound.value = endpoint;
  inboundSaveError.value = undefined;
  inboundEditorOpen.value = true;
}

async function submitInbound(draft: InboundWebhookDraft): Promise<void> {
  inboundSaveError.value = undefined;

  try {
    await saveInbound.mutateAsync({ id: editingInbound.value?.id ?? null, draft });
    inboundEditorOpen.value = false;
  } catch (failure) {
    inboundSaveError.value = [errorText(failure, t), ...fieldErrorLines(failure)].join(' ');
  }
}

function askRotateInbound(endpoint: InboundWebhook): void {
  rotatingOutbound.value = null;
  rotatingInbound.value = endpoint;
  rotateOpen.value = true;
}

function askRotateOutbound(endpoint: OutboundWebhook): void {
  rotatingInbound.value = null;
  rotatingOutbound.value = endpoint;
  rotateOpen.value = true;
}

async function confirmRotate(): Promise<void> {
  const inboundTarget = rotatingInbound.value;
  const outboundTarget = rotatingOutbound.value;

  await run(async () => {
    if (inboundTarget !== null) {
      await rotateInbound.mutateAsync(inboundTarget.id);
    }

    if (outboundTarget !== null) {
      revealedSecret.value = await rotateOutbound.mutateAsync(outboundTarget.id);
    }
  });

  rotateOpen.value = false;
}

async function sendTest(endpoint: OutboundWebhook): Promise<void> {
  testResult.value = null;

  await run(async () => {
    const outcome = await testOutbound.mutateAsync(endpoint.id);

    testResult.value = { name: endpoint.name, outcome };
  });
}

function openDeliveries(endpoint: OutboundWebhook): void {
  deliveriesFor.value = endpoint;
  deliveriesOpen.value = true;
}

function openEditor(endpoint: OutboundWebhook | null): void {
  editing.value = endpoint;
  saveError.value = undefined;
  editorOpen.value = true;
}

async function submit(draft: OutboundWebhookDraft): Promise<void> {
  saveError.value = undefined;

  try {
    revealedSecret.value = await save.mutateAsync({ id: editing.value?.id ?? null, draft });
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
    bodyTemplate: null,
    bodyIsJson: endpoint.bodyIsJson,
    customHeaders: null,
    isEnabled: !endpoint.isEnabled,
  };

  return run(async () => {
    await save.mutateAsync({ id: endpoint.id, draft });
  });
}

function flipInbound(endpoint: InboundWebhook): Promise<void> {
  return run(() => setInbound.mutateAsync({ id: endpoint.id, isEnabled: !endpoint.isEnabled }));
}

function categoryLabel(key: string): string {
  const path = `dashboard.settings.webhooks.delete.category.${key}`;

  return te(path)
    ? t(path)
    : key.replaceAll('_', ' ');
}

function consequenceLines(blast: WebhookBlastRadius) {
  const counted = blast.categories.filter((category) => Number(category.count) > 0);

  const lines = counted.map((category) =>
    t('dashboard.settings.webhooks.delete.line', {
      count: Number(category.count),
      what: categoryLabel(category.categoryKey),
    }),
  );

  if (lines.length === 0)
    return [t('dashboard.settings.webhooks.delete.nothing')] as const satisfies readonly string[];

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
  <PageHeader
    :title="$t('frame.nav.settingsPages.webhooks')"
    :crumb="$t('dashboard.settings.webhooks.crumb')"
  />

  <PanelBeside>
    <PageBody>
      <PanelCard
        data-view="webhooks-outbound"
        class="shrink-0"
        pad
        tier="lead"
        :title="$t('dashboard.settings.webhooks.outbound.title')"
        :description="$t('dashboard.settings.webhooks.outbound.description')"
        :state="outboundState"
        :empty-text="$t('dashboard.settings.webhooks.outbound.empty')"
        :error-text="$t('dashboard.settings.webhooks.outbound.loadFailed')"
        @retry="outbound.refetch()"
      >
        <template v-if="can('webhooks:outbound:write')" #actions>
          <Button data-new-outbound @click="openEditor(null)">
            <Add class="size-5" />
            {{ $t("dashboard.settings.webhooks.outbound.new") }}
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
            @test="sendTest(endpoint)"
            @rotate="askRotateOutbound(endpoint)"
            @deliveries="openDeliveries(endpoint)"
            @remove="askDeleteOutbound(endpoint)"
          />
        </RowGroup>
      </PanelCard>
      <PanelCard
        data-view="webhooks-inbound"
        class="shrink-0"
        pad
        :title="$t('dashboard.settings.webhooks.inbound.title')"
        :description="$t('dashboard.settings.webhooks.inbound.description')"
        :state="inboundState"
        :empty-text="$t('dashboard.settings.webhooks.inbound.empty')"
        :error-text="$t('dashboard.settings.webhooks.inbound.loadFailed')"
        @retry="inbound.refetch()"
      >
        <template v-if="can('webhooks:inbound:write')" #actions>
          <Button variant="outline" data-new-inbound @click="openInboundEditor(null)">
            <Add class="size-5" />
            {{ $t("dashboard.settings.webhooks.inbound.new") }}
          </Button>
        </template>
        <RowGroup>
          <InboundWebhookRow
            v-for="endpoint in inboundRows"
            :key="endpoint.id"
            :endpoint="endpoint"
            :busy="busy"
            @toggle="flipInbound(endpoint)"
            @edit="openInboundEditor(endpoint)"
            @rotate="askRotateInbound(endpoint)"
            @remove="askDeleteInbound(endpoint)"
          />
        </RowGroup>
      </PanelCard>
      <p
        v-if="testResult"
        data-test-result
        role="status"
        class="pt-3 text-sm"
        :class="testResult.outcome.delivered ? 'text-muted-foreground' : 'text-destructive'"
      >
        {{
          $t(
            testResult.outcome.delivered
              ? "dashboard.settings.webhooks.outbound.testDelivered"
              : "dashboard.settings.webhooks.outbound.testFailed",
            {
              name: testResult.name,
              code: testResult.outcome.responseCode ?? "-",
              ms: testResult.outcome.durationMs ?? "-",
              error: testResult.outcome.error ?? "",
            },
          )
        }}
      </p>
      <p v-if="pageError" role="alert" class="pt-3 text-sm text-destructive">{{ pageError }}</p>
      <EgressAllowlistCard v-if="can('egress:allowlist:read')" />
    </PageBody>
    <template v-if="can('webhooks:outbound:read')" #panel>
      <OutboundDeliveriesPanel v-model:open="deliveriesOpen" :endpoint="deliveriesFor" />
    </template>
  </PanelBeside>

  <OutboundWebhookDialog
    v-if="can('webhooks:outbound:write')"
    v-model:open="editorOpen"
    :endpoint="editing"
    :catalog="catalog.data.value ?? []"
    :pending="save.isPending.value"
    :error="saveError"
    @submit="submit"
  />
  <InboundWebhookDialog
    v-if="can('webhooks:inbound:write')"
    v-model:open="inboundEditorOpen"
    :endpoint="editingInbound"
    :pipelines="pipelines.data.value ?? []"
    :pending="saveInbound.isPending.value"
    :error="inboundSaveError"
    @submit="submitInbound"
  />
  <SigningSecretDialog :secret="revealedSecret" @done="revealedSecret = null" />
  <ConfirmDialog
    v-model:open="rotateOpen"
    :title="
      rotatingInbound
        ? $t('dashboard.settings.webhooks.inbound.rotateTitle', { name: rotatingInbound.name })
        : $t('dashboard.settings.webhooks.outbound.rotateTitle', {
          name: rotatingOutbound?.name ?? '',
        })
    "
    :description="
      rotatingInbound
        ? $t('dashboard.settings.webhooks.inbound.rotateDescription')
        : $t('dashboard.settings.webhooks.outbound.rotateDescription')
    "
    :action="$t('dashboard.settings.webhooks.rotateAction')"
    :pending="busy"
    @confirm="confirmRotate"
  />
  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="
      $t('dashboard.settings.webhooks.delete.title', {
        name: deletingOutbound?.name ?? deletingInbound?.name ?? '',
      })
    "
    :description="$t('dashboard.settings.webhooks.delete.description')"
    :consequences="consequences"
    :action="$t('dashboard.settings.webhooks.delete.action')"
    :pending="busy"
    @confirm="confirmDelete"
  />
</template>
