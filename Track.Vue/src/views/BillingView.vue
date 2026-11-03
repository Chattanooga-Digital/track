<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useSnapshotStore } from '@/stores/snapshotStore'
import { getBillingMonth, getBillingMonths } from '@/api/statsApi'
import { getBillingRates, updateBillingRates } from '@/api/billingApi'
import type { BillingCustomerDto, BillingMonthDto, BillingRateDto } from '@/types/stats'
import { formatBytes, formatMoney } from '@/utils/format'

const LIVE = 'live'

const snapshotStore = useSnapshotStore()
const { snapshot, customers } = storeToRefs(snapshotStore)

const period = ref(LIVE)
const months = ref<string[]>([])
const monthData = ref<BillingMonthDto | null>(null)
const loading = ref(false)

function monthTitle(month: string): string {
  return new Date(`${month}-01T00:00:00Z`).toLocaleDateString(undefined, {
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  })
}

const MONTHS_PINNED = 2

const olderMonths = computed(() => months.value.slice(MONTHS_PINNED))
const pinnedMonths = computed(() => {
  const pinned = months.value.slice(0, MONTHS_PINNED)
  return olderMonths.value.includes(period.value) ? [...pinned, period.value] : pinned
})

const isLive = computed(() => period.value === LIVE)

onMounted(async () => {
  months.value = await getBillingMonths()
})

watch(period, async (value) => {
  monthData.value = null
  if (value === LIVE) return
  loading.value = true
  try {
    monthData.value = await getBillingMonth(value)
  } finally {
    loading.value = false
  }
})

// A common row shape for both sources: the live snapshot's customer groups and a
// past month's per-customer averages.
interface BillingRow {
  customerId: number | null
  customerName: string | null
  isShared: boolean
  diskBytes: number
  objectStorageBytes: number
  blendedPercentOfCluster: number
  computeCost: number
  diskCost: number
  objectStorageCost: number
  totalCost: number
}

const allRows = computed<BillingRow[]>(() =>
  isLive.value
    ? customers.value.map((c) => ({
        customerId: c.customerId,
        customerName: c.customerName,
        isShared: c.isShared,
        diskBytes: c.totals.diskBytes,
        objectStorageBytes: c.totals.objectStorageBytes,
        blendedPercentOfCluster: c.totals.blendedPercentOfCluster,
        computeCost: c.totals.computeCost,
        diskCost: c.totals.diskCost,
        objectStorageCost: c.totals.objectStorageCost,
        totalCost: c.totals.totalCost,
      }))
    : ((monthData.value?.customers ?? []) as BillingCustomerDto[]),
)

const rows = computed(() =>
  allRows.value.filter((c) => c.customerId !== null).sort((a, b) => b.totalCost - a.totalCost),
)

const shared = computed(() => allRows.value.find((c) => c.isShared))
const unassigned = computed(() => allRows.value.find((c) => c.customerId === null && !c.isShared))
const unbilledRows = computed(() =>
  [shared.value, unassigned.value]
    .filter((row): row is BillingRow => row !== undefined)
    .map((row) => ({
      ...row,
      name: row.isShared ? 'Shared infrastructure' : 'Unassigned stacks',
    })),
)

const currency = computed(
  () => (isLive.value ? snapshot.value?.currency : monthData.value?.currency) ?? 'USD',
)

const capacity = computed(() =>
  isLive.value
    ? snapshot.value?.cluster
    : monthData.value && monthData.value.sampleCount > 0
      ? monthData.value.capacity
      : undefined,
)

// The live snapshot carries precomputed grand totals; a month's totals come off the model.
const totals = computed(() => {
  if (isLive.value) return snapshot.value?.totals
  return monthData.value ?? undefined
})
const totalBlended = computed(() =>
  isLive.value
    ? (snapshot.value?.totals.blendedPercentOfCluster ?? 0)
    : allRows.value.reduce((sum, c) => sum + c.blendedPercentOfCluster, 0),
)
const totalBilled = computed(() => totals.value?.totalCost ?? 0)
const billedShare = computed(() =>
  rows.value.reduce((sum, c) => sum + c.blendedPercentOfCluster, 0),
)

function shareOfCompute(row: BillingRow): number {
  const compute = totals.value?.computeCost ?? 0
  return compute > 0 ? (row.computeCost / compute) * 100 : 0
}

const noMonthData = computed(
  () => !isLive.value && !loading.value && (monthData.value?.sampleCount ?? 0) === 0,
)

function pct(value: number): string {
  return `${value.toFixed(2)}%`
}

function money(value: number | null | undefined): string {
  return formatMoney(value, currency.value)
}

function cores(value: number | undefined): string {
  if (value === undefined) return '0'
  return Number.isInteger(value) ? `${value}` : value.toFixed(1)
}

// --- Rate editor ---
const rateDialog = ref(false)
const savingRate = ref(false)
const rateForm = reactive<BillingRateDto>({
  clusterComputeMonthlyCost: 0,
  diskRatePerGbMonth: 0,
  objectStorageRatePerGbMonth: 0,
  currency: 'USD',
})

async function openRateDialog() {
  const rate = await getBillingRates()
  Object.assign(rateForm, rate)
  rateDialog.value = true
}

async function saveRates() {
  savingRate.value = true
  try {
    await updateBillingRates({
      clusterComputeMonthlyCost: Number(rateForm.clusterComputeMonthlyCost) || 0,
      diskRatePerGbMonth: Number(rateForm.diskRatePerGbMonth) || 0,
      objectStorageRatePerGbMonth: Number(rateForm.objectStorageRatePerGbMonth) || 0,
      currency: rateForm.currency.trim() || 'USD',
    })
    rateDialog.value = false
    // Live re-prices on the next snapshot push; a shown month must be re-fetched to reflect the new rate.
    if (!isLive.value) monthData.value = await getBillingMonth(period.value)
  } finally {
    savingRate.value = false
  }
}
</script>

<template>
  <div class="track-page">
    <div class="d-flex flex-wrap align-end justify-space-between ga-4 mb-6">
      <h1 class="text-h5 font-weight-medium">Billing</h1>
      <div class="d-flex flex-wrap align-center ga-2">
        <v-btn-toggle
          v-model="period"
          mandatory
          divided
          variant="outlined"
          density="comfortable"
          aria-label="Billing period"
        >
          <v-btn :value="LIVE">Live</v-btn>
          <v-btn v-for="m in pinnedMonths" :key="m" :value="m">{{ monthTitle(m) }}</v-btn>
        </v-btn-toggle>
        <v-menu v-if="olderMonths.length > 0">
          <template #activator="{ props }">
            <v-btn v-bind="props" variant="text" append-icon="mdi-chevron-down">Earlier</v-btn>
          </template>
          <v-list density="compact">
            <v-list-item
              v-for="m in olderMonths"
              :key="m"
              :title="monthTitle(m)"
              :active="period === m"
              @click="period = m"
            />
          </v-list>
        </v-menu>
        <v-btn variant="outlined" prepend-icon="mdi-tune-variant" @click="openRateDialog"
          >Rates</v-btn
        >
      </div>
    </div>

    <div class="stat-grid mb-6">
      <v-card variant="flat" border class="pa-4">
        <div class="text-caption text-medium-emphasis">Total billed</div>
        <div class="text-h5 font-weight-medium my-1">{{ money(totalBilled) }}</div>
        <div class="text-caption text-medium-emphasis">
          {{ isLive ? 'Per month' : `${monthData?.sampleCount ?? 0} samples` }}
        </div>
      </v-card>
      <v-card variant="flat" border class="pa-4">
        <div class="text-caption text-medium-emphasis">
          {{ isLive ? 'Cluster utilization' : 'Average utilization' }}
        </div>
        <div class="text-h5 font-weight-medium my-1">{{ pct(totalBlended) }}</div>
        <div class="text-caption text-medium-emphasis">CPU and memory</div>
      </v-card>
      <v-card variant="flat" border class="pa-4">
        <div class="d-flex align-center justify-space-between ga-2">
          <span class="text-caption text-medium-emphasis">Shared infrastructure</span>
          <v-chip size="x-small" variant="tonal">Not billed</v-chip>
        </div>
        <div class="text-h5 font-weight-medium my-1">
          {{ pct(shared?.blendedPercentOfCluster ?? 0) }}
        </div>
      </v-card>
      <v-card variant="flat" border class="pa-4">
        <div class="d-flex align-center justify-space-between ga-2">
          <span class="text-caption text-medium-emphasis">Unassigned</span>
          <v-chip size="x-small" variant="tonal" color="warning">Not billed</v-chip>
        </div>
        <div
          class="text-h5 font-weight-medium my-1"
          :class="{ 'text-warning': (unassigned?.blendedPercentOfCluster ?? 0) > 0 }"
        >
          {{ pct(unassigned?.blendedPercentOfCluster ?? 0) }}
        </div>
      </v-card>
      <v-card variant="flat" border class="pa-4">
        <div class="text-caption text-medium-emphasis">
          {{ isLive ? 'Capacity' : 'Average capacity' }}
        </div>
        <div class="text-h5 font-weight-medium my-1">
          {{ cores(capacity?.cpuCores) }} vCPU · {{ formatBytes(capacity?.memoryBytes) }}
        </div>
        <div class="text-caption text-medium-emphasis">
          {{ cores(capacity?.nodeCount) }} node{{ capacity?.nodeCount === 1 ? '' : 's' }}
        </div>
      </v-card>
    </div>

    <v-card variant="flat" border :loading="loading">
      <div class="table-scroll">
        <div role="table" aria-label="Charges by customer">
          <div
            role="row"
            class="billing-grid header-row text-caption text-medium-emphasis row-border"
          >
            <div role="columnheader">Customer</div>
            <div role="columnheader">Share of cluster</div>
            <div role="columnheader" class="text-right">Compute</div>
            <div role="columnheader" class="text-right">Disk</div>
            <div role="columnheader" class="text-right">Object storage</div>
            <div role="columnheader" class="text-right">Total</div>
          </div>

          <div
            v-for="c in rows"
            :key="c.customerId ?? 'none'"
            role="row"
            class="billing-grid customer-row row-border"
          >
            <div role="cell" class="font-weight-medium">{{ c.customerName }}</div>
            <div role="cell">
              <div class="d-flex justify-space-between text-body-2 mb-1">
                <span>{{ pct(c.blendedPercentOfCluster) }}</span>
                <span class="text-medium-emphasis"
                  >{{ shareOfCompute(c).toFixed(1) }}% of compute</span
                >
              </div>
              <v-progress-linear
                :model-value="shareOfCompute(c)"
                color="primary"
                height="6"
                rounded
              />
            </div>
            <div role="cell" class="text-right">{{ money(c.computeCost) }}</div>
            <div role="cell" class="text-right">
              <div>{{ money(c.diskCost) }}</div>
              <div class="text-caption text-medium-emphasis">{{ formatBytes(c.diskBytes) }}</div>
            </div>
            <div role="cell" class="text-right">
              <div>{{ money(c.objectStorageCost) }}</div>
              <div class="text-caption text-medium-emphasis">
                {{ formatBytes(c.objectStorageBytes) }}
              </div>
            </div>
            <div role="cell" class="text-right text-subtitle-1 font-weight-medium">
              {{ money(c.totalCost) }}
            </div>
          </div>

          <div
            v-for="u in unbilledRows"
            :key="u.name"
            role="row"
            class="billing-grid customer-row row-border text-medium-emphasis"
          >
            <div role="cell" class="d-flex flex-wrap align-center ga-2">
              <span class="font-weight-medium">{{ u.name }}</span>
              <v-chip size="x-small" variant="tonal" :color="u.isShared ? undefined : 'warning'"
                >Not billed</v-chip
              >
            </div>
            <div role="cell" class="text-body-2">{{ pct(u.blendedPercentOfCluster) }}</div>
            <div role="cell" class="text-right">{{ money(u.computeCost) }}</div>
            <div role="cell" class="text-right">{{ formatBytes(u.diskBytes) }}</div>
            <div role="cell" class="text-right">{{ formatBytes(u.objectStorageBytes) }}</div>
            <div role="cell" class="text-right">{{ money(u.totalCost) }}</div>
          </div>

          <div v-if="noMonthData" class="text-center text-medium-emphasis py-6">
            No data for this month.
          </div>
          <div
            v-else-if="rows.length === 0 && !loading"
            class="text-center text-medium-emphasis py-6"
          >
            No customers yet.
          </div>

          <div v-if="rows.length > 0" role="row" class="billing-grid total-row font-weight-medium">
            <div role="cell">Total</div>
            <div role="cell" class="text-body-2 text-medium-emphasis">
              {{ pct(billedShare) }} billed · {{ pct(shared?.blendedPercentOfCluster ?? 0) }} shared
              · {{ pct(unassigned?.blendedPercentOfCluster ?? 0) }} unassigned
            </div>
            <div role="cell" class="text-right">{{ money(totals?.computeCost) }}</div>
            <div role="cell" class="text-right">{{ money(totals?.diskCost) }}</div>
            <div role="cell" class="text-right">{{ money(totals?.objectStorageCost) }}</div>
            <div role="cell" class="text-right text-subtitle-1 font-weight-medium">
              {{ money(totalBilled) }}
            </div>
          </div>
        </div>
      </div>
    </v-card>

    <p class="text-body-2 text-medium-emphasis mt-4">
      Cluster cost is split by CPU and memory share. Storage bills per GB.
    </p>

    <v-dialog v-model="rateDialog" max-width="480">
      <v-card>
        <v-card-title>Billing rates</v-card-title>
        <v-card-text>
          <v-text-field
            v-model.number="rateForm.clusterComputeMonthlyCost"
            type="number"
            min="0"
            label="Cluster cost per month"
            variant="outlined"
            density="comfortable"
            class="mb-3"
          />
          <v-text-field
            v-model.number="rateForm.diskRatePerGbMonth"
            type="number"
            min="0"
            label="Disk per GB per month"
            hint="0 to include in cluster cost"
            persistent-hint
            variant="outlined"
            density="comfortable"
            class="mb-3"
          />
          <v-text-field
            v-model.number="rateForm.objectStorageRatePerGbMonth"
            type="number"
            min="0"
            label="Object storage per GB per month"
            variant="outlined"
            density="comfortable"
            class="mb-3"
          />
          <v-text-field
            v-model="rateForm.currency"
            label="Currency"
            variant="outlined"
            density="comfortable"
          />
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="rateDialog = false">Cancel</v-btn>
          <v-btn color="primary" variant="flat" :loading="savingRate" @click="saveRates"
            >Save</v-btn
          >
        </v-card-actions>
      </v-card>
    </v-dialog>
  </div>
</template>

<style scoped>
.stat-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 16px;
}

.row-border {
  border-bottom: thin solid rgba(var(--v-border-color), var(--v-border-opacity));
}

.table-scroll {
  overflow-x: auto;
}

.table-scroll > [role='table'] {
  min-width: 760px;
}

.billing-grid {
  display: grid;
  grid-template-columns: minmax(200px, 1fr) 200px 110px 110px 130px 120px;
  gap: 16px;
  align-items: center;
  padding: 0 20px;
}

.header-row {
  padding-top: 12px;
  padding-bottom: 12px;
}

.customer-row {
  min-height: 60px;
}

.total-row {
  min-height: 52px;
  background: rgba(var(--v-theme-on-surface), 0.03);
}
</style>
