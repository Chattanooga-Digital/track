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

const periodItems = computed(() => [
  { title: 'Live', value: LIVE },
  ...months.value.map((m) => ({ title: monthTitle(m), value: m })),
])

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

const unassigned = computed(() => allRows.value.find((c) => c.customerId === null))

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
  <v-container fluid>
    <div class="d-flex align-center mb-4 flex-wrap ga-4">
      <h1 class="text-h5">Billing &mdash; Cost Recovery</h1>
      <v-spacer />
      <v-btn variant="tonal" prepend-icon="mdi-cog" @click="openRateDialog">Rates</v-btn>
      <v-select
        v-model="period"
        :items="periodItems"
        label="Billing period"
        density="compact"
        variant="outlined"
        hide-details
        style="max-width: 220px"
      />
    </div>

    <v-row class="mb-2">
      <v-col cols="12" md="3">
        <v-card variant="outlined">
          <v-card-text>
            <div class="text-caption text-medium-emphasis">
              {{ isLive ? 'Cluster capacity' : 'Average cluster capacity' }}
            </div>
            <div class="text-h6">
              {{ cores(capacity?.cpuCores) }} vCPU &middot; {{ formatBytes(capacity?.memoryBytes) }}
            </div>
            <div class="text-caption text-medium-emphasis">
              {{ cores(capacity?.nodeCount) }} node(s)
            </div>
          </v-card-text>
        </v-card>
      </v-col>
      <v-col cols="12" md="3">
        <v-card variant="outlined">
          <v-card-text>
            <div class="text-caption text-medium-emphasis">Total billed</div>
            <div class="text-h4">{{ money(totalBilled) }}</div>
            <div class="text-caption text-medium-emphasis">Recovered across customers</div>
          </v-card-text>
        </v-card>
      </v-col>
      <v-col cols="12" md="3">
        <v-card variant="outlined">
          <v-card-text>
            <div class="text-caption text-medium-emphasis">
              {{ isLive ? 'Cluster utilization' : 'Average utilization' }}
            </div>
            <div class="text-h4">{{ pct(totalBlended) }}</div>
            <div class="text-caption text-medium-emphasis">Blended CPU + memory</div>
          </v-card-text>
        </v-card>
      </v-col>
      <v-col cols="12" md="3">
        <v-card variant="outlined">
          <v-card-text>
            <div class="text-caption text-medium-emphasis">Unassigned</div>
            <div class="text-h4">{{ pct(unassigned?.blendedPercentOfCluster ?? 0) }}</div>
            <div class="text-caption text-medium-emphasis">
              Overhead spread across customers via the base rate
            </div>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <v-card variant="outlined" :loading="loading">
      <v-table>
        <thead>
          <tr>
            <th>Customer</th>
            <th class="text-right">Blended %</th>
            <th class="text-right">Compute</th>
            <th class="text-right">Disk</th>
            <th class="text-right">Disk cost</th>
            <th class="text-right">Object storage</th>
            <th class="text-right">Object cost</th>
            <th class="text-right">Total</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="c in rows" :key="c.customerId ?? 'none'">
            <td class="font-weight-medium">{{ c.customerName }}</td>
            <td class="text-right">{{ pct(c.blendedPercentOfCluster) }}</td>
            <td class="text-right">{{ money(c.computeCost) }}</td>
            <td class="text-right">{{ formatBytes(c.diskBytes) }}</td>
            <td class="text-right">{{ money(c.diskCost) }}</td>
            <td class="text-right">{{ formatBytes(c.objectStorageBytes) }}</td>
            <td class="text-right">{{ money(c.objectStorageCost) }}</td>
            <td class="text-right font-weight-bold">{{ money(c.totalCost) }}</td>
          </tr>
          <tr v-if="noMonthData">
            <td colspan="8" class="text-center text-medium-emphasis py-6">
              No billing samples recorded for this month.
            </td>
          </tr>
          <tr v-else-if="rows.length === 0 && !loading">
            <td colspan="8" class="text-center text-medium-emphasis py-6">
              No customers yet. Assign stacks to customers to see billing shares.
            </td>
          </tr>
        </tbody>
        <tfoot v-if="rows.length > 0">
          <tr class="font-weight-bold">
            <td>Total</td>
            <td class="text-right">{{ pct(totalBlended) }}</td>
            <td class="text-right">{{ money(totals?.computeCost) }}</td>
            <td></td>
            <td class="text-right">{{ money(totals?.diskCost) }}</td>
            <td></td>
            <td class="text-right">{{ money(totals?.objectStorageCost) }}</td>
            <td class="text-right">{{ money(totalBilled) }}</td>
          </tr>
        </tfoot>
      </v-table>
    </v-card>

    <p class="text-caption text-medium-emphasis mt-3">
      <template v-if="isLive">
        Compute is billed as a utility: the cluster's monthly cost is recovered in full from paying
        customers, split by blended CPU + memory footprint, so idle capacity and unassigned overhead
        are spread across customers rather than absorbed. Disk and object storage bill at a flat
        per-GB rate. Set the rates with the button above. Pick a month for the billable average.
      </template>
      <template v-else>
        Month figures are the average of {{ monthData?.sampleCount ?? 0 }} billing samples across
        the month, priced at the current rates. A customer present for part of the month is averaged
        over the whole month, so partial months bill proportionally.
      </template>
    </p>

    <v-dialog v-model="rateDialog" max-width="480">
      <v-card>
        <v-card-title>Billing rates</v-card-title>
        <v-card-text>
          <v-text-field
            v-model.number="rateForm.clusterComputeMonthlyCost"
            type="number"
            min="0"
            label="Cluster compute cost (per month)"
            hint="What the whole cluster costs you monthly; recovered in full from customers."
            persistent-hint
            variant="outlined"
            density="comfortable"
            class="mb-3"
          />
          <v-text-field
            v-model.number="rateForm.diskRatePerGbMonth"
            type="number"
            min="0"
            label="Node disk rate (per GB / month)"
            hint="Set to 0 to treat local disk as bundled into the compute cost."
            persistent-hint
            variant="outlined"
            density="comfortable"
            class="mb-3"
          />
          <v-text-field
            v-model.number="rateForm.objectStorageRatePerGbMonth"
            type="number"
            min="0"
            label="Object storage rate (per GB / month)"
            variant="outlined"
            density="comfortable"
            class="mb-3"
          />
          <v-text-field
            v-model="rateForm.currency"
            label="Currency (ISO code)"
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
  </v-container>
</template>
