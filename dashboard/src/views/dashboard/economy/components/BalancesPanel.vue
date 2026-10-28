<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The viewers' wallets: who holds how much. A moderator with the keys can freeze a wallet (it stops
// earning and spending) or adjust its balance with a reason.
import { refDebounced } from '@vueuse/core';
import Coins from '~icons/nnz/coins';
import { computed, ref } from 'vue';

import { useAccounts, useFreezeAccount, type CurrencyAccount } from '@/api/economy';
import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
import ShowMoreFooter from '@/components/layout/ShowMoreFooter.vue';
import UserName from '@/components/layout/UserName.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { useShowMore } from '@/composables/useShowMore';

import AdjustDialog from './AdjustDialog.vue';

const SEARCH_PAUSE_MS = 250;

const { channelId } = useCurrentChannel();
const { can } = useCan();

const search = ref('');
const typed = refDebounced(search, SEARCH_PAUSE_MS);
const accounts = useAccounts(channelId, typed);
const freeze = useFreezeAccount(channelId);

const state = usePanelState(
  accounts,
  (data: readonly CurrencyAccount[]): boolean => data.length === 0,
);
const shown = useShowMore(() => accounts.data.value ?? []);

const adjusting = ref<CurrencyAccount | null>(null);
const adjustOpen = computed<boolean>({
  get: () => adjusting.value !== null,
  set: (open: boolean): void => {
    if (!open) {
      adjusting.value = null;
    }
  },
});

function toggle(row: CurrencyAccount): void {
  freeze.mutate({ viewerUserId: row.viewerUserId, frozen: !row.isFrozen });
}
</script>

<template>
  <div class="flex max-w-2xl flex-col gap-3">
    <SearchField v-model="search" :placeholder="$t('dashboard.economy.balances.search')" />
    <PanelCard
      :title="$t('dashboard.economy.balances.title')"
      :description="$t('dashboard.economy.balances.description')"
      :state="state"
      :empty-text="$t('dashboard.economy.balances.empty')"
      :error-text="$t('dashboard.economy.balances.error')"
      @retry="accounts.refetch()"
    >
      <ul class="flex flex-col">
        <li
          v-for="row in shown.visible.value"
          :key="row.id"
          data-account-row
          :data-account-name="row.viewerDisplayName"
          class="flex min-h-11 items-center gap-3 rounded-[8px] px-3 py-2"
        >
          <span class="min-w-0 flex-1">
            <UserName
              :user-id="row.viewerTwitchUserId"
              :name="row.viewerDisplayName"
              class="block text-sm wrap-break-word"
            />
            <span class="block text-xs text-muted-foreground">
              {{
                $t("dashboard.economy.balances.lifetime", {
                  earned: Number(row.lifetimeEarned),
                  spent: Number(row.lifetimeSpent),
                })
              }}
            </span>
          </span>
          <span class="text-sm font-medium tabular-nums">{{ Number(row.balance) }}</span>
          <Button
            v-if="can('economy:account:adjust')"
            data-account-adjust
            type="button"
            variant="ghost"
            size="icon"
            :aria-label="$t('dashboard.economy.balances.adjust', { name: row.viewerDisplayName })"
            @click="adjusting = row"
          >
            <Coins aria-hidden="true" class="size-5" />
          </Button>
          <GatedSwitch
            action="economy:account:freeze"
            :model-value="row.isFrozen"
            :aria-label="$t('dashboard.economy.balances.freeze', { name: row.viewerDisplayName })"
            @update:model-value="toggle(row)"
          />
        </li>
      </ul>
      <template v-if="shown.hasMore.value" #footer>
        <ShowMoreFooter :remaining="shown.remaining.value" @more="shown.more()" />
      </template>
    </PanelCard>
    <AdjustDialog v-model:open="adjustOpen" :account="adjusting" />
  </div>
</template>
