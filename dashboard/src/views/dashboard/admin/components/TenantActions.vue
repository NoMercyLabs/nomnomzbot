<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// What an admin does to one tenant. Lifecycle (suspend or reinstate, begin access) is the quiet group;
// re-migrate and export are plain; erase is the one destructive action and sits alone, apart.
// Every write asks for a justification, and the bot keeps it in the audit log.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import Download from '~icons/nnz/download';
import KeyModern from '~icons/nnz/key-modern';
import Pause from '~icons/nnz/pause';
import Play from '~icons/nnz/play';
import DataBaseRefresh from '~icons/nnz/data-base-refresh';
import Trash from '~icons/nnz/trash';

import {
  useAdminBeginAccess,
  useAdminReinstate,
  useAdminSuspend,
  type AdminTenantDetail,
} from '@/api/admin';
import {
  useEraseTenant,
  useExportTenant,
  useRemigrateTenant,
  useTenantErasePreview,
} from '@/api/adminTenants';
import FormDialog from '@/components/layout/FormDialog.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Switch } from '@/components/ui/switch';
import { Textarea } from '@/components/ui/textarea';
import { usePlatformCan } from '@/composables/usePlatformCan';

type Action = 'suspend' | 'reinstate' | 'access' | 'remigrate' | 'erase';

const props = defineProps<{ tenant: AdminTenantDetail }>();

const { t } = useI18n();
const { can } = usePlatformCan();

const action = ref<Action | null>(null);
const text = ref('');
const newStatus = ref<'suspended' | 'platform_banned'>('suspended');
const breakGlass = ref(false);
const expires = ref('');
const result = ref('');

const suspend = useAdminSuspend();
const reinstate = useAdminReinstate();
const access = useAdminBeginAccess();
const remigrate = useRemigrateTenant();
const erase = useEraseTenant();
const exporter = useExportTenant();
const preview = useTenantErasePreview(
  () => props.tenant.id,
  () => action.value === 'erase',
);

const isActive = computed(() => props.tenant.status === 'active');
const open = computed(() => action.value !== null);
const error = ref('');

const statusOptions = computed(() =>
  (['suspended', 'platform_banned'] as const).map((value) => ({
    value,
    label: t(`dashboard.admin.tenants.suspend.status.${value}`),
  })),
);

function begin(next: Action): void {
  text.value = '';
  error.value = '';
  result.value = '';
  action.value = next;
}

function close(value: boolean): void {
  if (!value) {
    action.value = null;
  }
}

function fail(cause: Error): void {
  error.value = cause.message;
}

function done(message = ''): void {
  result.value = message;
  action.value = null;
}

function submit(): void {
  const broadcasterId = props.tenant.id;
  const justification = text.value.trim();

  if (justification === '') {
    error.value = t('dashboard.admin.tenants.justificationRequired');

    return;
  }

  error.value = '';
  run(broadcasterId, justification);
}

function run(broadcasterId: string, justification: string): void {
  const handlers = {
    suspend: () =>
      suspend.mutate(
        {
          broadcasterId,
          newStatus: newStatus.value,
          reason: justification,
        },
        { onSuccess: () => done(), onError: fail },
      ),
    reinstate: () =>
      reinstate.mutate(
        { broadcasterId, justification },
        { onSuccess: () => done(), onError: fail },
      ),
    access: () =>
      access.mutate(
        {
          broadcasterId,
          justification,
          breakGlass: breakGlass.value,
          expiresAt: expires.value === ''
            ? null
            : new Date(expires.value).toISOString(),
        },
        { onSuccess: (grant) => done(grant.id), onError: fail },
      ),
    remigrate: () =>
      remigrate.mutate(
        { broadcasterId, justification },
        {
          onSuccess: (outcome) => done(outcome.migrationsAppliedThisCall.join(', ')),
          onError: fail,
        },
      ),
    erase: () =>
      erase.mutate({ broadcasterId, justification }, { onSuccess: () => done(), onError: fail }),
  } as const satisfies Record<Action, () => void>;

  handlers[action.value!]();
}

function download(body: string): void {
  const blob = new Blob([body], { type: 'application/json' });
  const link = document.createElement('a');

  link.href = URL.createObjectURL(blob);
  link.download = `${props.tenant.name}.json`;
  link.click();
  URL.revokeObjectURL(link.href);
}

function exportData(): void {
  exporter.mutate(props.tenant.id, { onSuccess: download });
}

const pending = computed(
  () =>
    suspend.isPending.value ||
    reinstate.isPending.value ||
    access.isPending.value ||
    remigrate.isPending.value ||
    erase.isPending.value,
);
</script>

<template>
  <div data-tenant-actions class="flex flex-wrap items-center gap-2">
    <template v-if="can('tenant:suspend')">
      <Button v-if="isActive" variant="outline" size="sm" @click="begin('suspend')">
        <Pause aria-hidden="true" />
        {{ t('dashboard.admin.tenants.actions.suspend') }}
      </Button>
      <Button v-else variant="outline" size="sm" @click="begin('reinstate')">
        <Play aria-hidden="true" />
        {{ t('dashboard.admin.tenants.actions.reinstate') }}
      </Button>
    </template>
    <Button v-if="can('tenant:access')" variant="outline" size="sm" @click="begin('access')">
      <KeyModern aria-hidden="true" />
      {{ t('dashboard.admin.tenants.actions.access') }}
    </Button>
    <Button v-if="can('tenant:remigrate')" variant="outline" size="sm" @click="begin('remigrate')">
      <DataBaseRefresh aria-hidden="true" />
      {{ t('dashboard.admin.tenants.actions.remigrate') }}
    </Button>
    <Button v-if="can('tenant:erase')"
            variant="outline"
            size="sm"
            :disabled="exporter.isPending.value"
            @click="exportData"
    >
      <Download aria-hidden="true" />
      {{ t('dashboard.admin.tenants.actions.export') }}
    </Button>
    <Button v-if="can('tenant:erase')"
            class="ml-auto"
            variant="destructive"
            size="sm"
            @click="begin('erase')"
    >
      <Trash aria-hidden="true" />
      {{ t('dashboard.admin.tenants.actions.erase') }}
    </Button>
  </div>
  <p v-if="result" role="status" class="text-sm text-muted-foreground">
    {{ t('dashboard.admin.tenants.result', { value: result }) }}
  </p>
  <FormDialog
    :open="open"
    :title="action ? t(`dashboard.admin.tenants.${action}.title`, { name: tenant.name }) : ''"
    :description="action ? t(`dashboard.admin.tenants.${action}.description`) : undefined"
    :action="action ? t(`dashboard.admin.tenants.actions.${action}`) : ''"
    :pending="pending"
    :error="error"
    @update:open="close"
    @submit="submit"
  >
    <div v-if="action === 'suspend'" class="grid gap-2">
      <Label>{{ t('dashboard.admin.tenants.suspend.statusLabel') }}</Label>
      <SegmentedControl
        v-model="newStatus"
        :label="t('dashboard.admin.tenants.suspend.statusLabel')"
        :options="statusOptions"
      />
    </div>
    <div v-if="action === 'erase' && preview.data.value" data-erase-preview class="grid gap-1 text-sm">
      <p>
        {{
          t('dashboard.admin.tenants.erase.window', {
            days: preview.data.value.restoreWindowDays,
          })
        }}
      </p>
      <ul class="list-disc pl-5">
        <li
          v-for="category in preview.data.value.blastRadius.categories"
          :key="category.categoryKey"
        >
          {{ category.categoryKey }}{{ ' ' }}{{ category.count }}
        </li>
      </ul>
    </div>
    <div class="grid gap-2">
      <Label for="tenant-justification">{{ t('dashboard.admin.tenants.justification') }}</Label>
      <Textarea id="tenant-justification"
                v-model="text"
                data-justification
                data-suspend-reason
                rows="3"
      />
    </div>
    <div v-if="action === 'access' && can('tenant:access')" class="grid gap-3">
      <div data-touch-target class="flex min-h-9 items-center justify-between gap-3">
        <Label for="tenant-break-glass">{{ t('dashboard.admin.tenants.access.breakGlass') }}</Label>
        <Switch id="tenant-break-glass" v-model="breakGlass" />
      </div>
      <div class="grid gap-2">
        <Label for="tenant-expires">{{ t('dashboard.admin.tenants.access.expires') }}</Label>
        <Input id="tenant-expires" v-model="expires" type="datetime-local" />
      </div>
    </div>
  </FormDialog>
</template>
