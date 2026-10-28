<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The billing desk: tiers, invite codes, one channel's invoices and grants, and the priced usage units.
// Every list reloads when another operator changes billing (the ReceiveBillingChanged push).
// Hosting tenants for third parties is the restricted saas mode, so the page says so.
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';

import BillingChannelPanel from './components/BillingChannelPanel.vue';
import BillingInvitesPanel from './components/BillingInvitesPanel.vue';
import BillingPricedUnitsPanel from './components/BillingPricedUnitsPanel.vue';
import BillingTiersPanel from './components/BillingTiersPanel.vue';
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.billing.title')" :crumb="$t('dashboard.admin.crumb')" />
  <PageBody data-page-body>
    <p data-saas-restricted class="text-sm text-muted-foreground">
      {{ $t("dashboard.admin.billing.restricted") }}
    </p>
    <BillingTiersPanel />
    <BillingInvitesPanel />
    <BillingChannelPanel />
    <BillingPricedUnitsPanel />
  </PageBody>
</template>
