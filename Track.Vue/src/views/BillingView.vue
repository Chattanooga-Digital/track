<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useSnapshotStore } from '@/stores/snapshotStore'
import { getBillingMonth, getBillingMonths } from '@/api/statsApi'
import type { BillingCustomerDto, BillingMonthDto } from '@/types/stats'
import { formatBytes } from '@/utils/format'

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
  cpuPercent: number
  memoryUsedBytes: number
  diskBytes: number
  objectStorageBytes: number
  cpuPercentOfCluster: number
  memoryPercentOfCluster: number
  blendedPercentOfCluster: number
}

const allRows = computed<BillingRow[]>(() =>
  isLive.value
    ? customers.value.map((c) => ({
        customerId: c.customerId,
        customerName: c.customerName,
        cpuPercent: c.totals.cpuPercent,
        memoryUsedBytes: c.totals.memoryUsedBytes,
        diskBytes: c.totals.diskBytes,
        objectStorageBytes: c.totals.objectStorageBytes,
        cpuPercentOfCluster: c.totals.cpuPercentOfCluster,
        memoryPercentOfCluster: c.totals.memoryPercentOfCluster,
        blendedPercentOfCluster: c.totals.blendedPercentOfCluster,
      }))
    : ((monthData.value?.customers ?? []) as BillingCustomerDto[]),
)

const rows = computed(() =>
  allRows.value
    .filter((c) => c.customerId !== null)
    .sort((a, b) => b.blendedPercentOfCluster - a.blendedPercentOfCluster),
)

const unassigned = computed(() => allRows.value.find((c) => c.customerId === null))

const capacity = computed(() =>
  isLive.value
    ? snapshot.value?.cluster
    : monthData.value && monthData.value.sampleCount > 0
      ? monthData.value.capacity
      : undefined,
)

// The live snapshot carries precomputed grand totals; a month's totals are the sum of its rows.
const totalBlended = computed(() =>
  isLive.value
    ? (snapshot.value?.totals.blendedPercentOfCluster ?? 0)
    : allRows.value.reduce((sum, c) => sum + c.blendedPercentOfCluster, 0),
)
const totalCpu = computed(() =>
  isLive.value
    ? (snapshot.value?.totals.cpuPercentOfCluster ?? 0)
    : allRows.value.reduce((sum, c) => sum + c.cpuPercentOfCluster, 0),
)
const totalMemory = computed(() =>
  isLive.value
    ? (snapshot.value?.totals.memoryPercentOfCluster ?? 0)
    : allRows.value.reduce((sum, c) => sum + c.memoryPercentOfCluster, 0),
)

const noMonthData = computed(
  () => !isLive.value && !loading.value && (monthData.value?.sampleCount ?? 0) === 0,
)

function pct(value: number): string {
  return `${value.toFixed(2)}%`
}

function cores(value: number | undefined): string {
  if (value === undefined) return '0'
  return Number.isInteger(value) ? `${value}` : value.toFixed(1)
}
</script>

<template>
  <v-container fluid>
    <div class="d-flex align-center mb-4 flex-wrap ga-4">
      <h1 class="text-h5">Billing &mdash; Blended Cluster Utilization</h1>
      <v-spacer />
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
      <v-col cols="12" md="4">
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
      <v-col cols="12" md="4">
        <v-card variant="outlined">
          <v-card-text>
            <div class="text-caption text-medium-emphasis">
              {{ isLive ? 'Total blended utilization' : 'Average blended utilization' }}
            </div>
            <div class="text-h4">{{ pct(totalBlended) }}</div>
            <div class="text-caption text-medium-emphasis">
              CPU {{ pct(totalCpu) }} &middot; Mem {{ pct(totalMemory) }}
            </div>
          </v-card-text>
        </v-card>
      </v-col>
      <v-col cols="12" md="4">
        <v-card variant="outlined">
          <v-card-text>
            <div class="text-caption text-medium-emphasis">Unassigned (unbilled)</div>
            <div class="text-h4">{{ pct(unassigned?.blendedPercentOfCluster ?? 0) }}</div>
            <div class="text-caption text-medium-emphasis">
              Stacks and buckets not mapped to a customer
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
            <th class="text-right">CPU %</th>
            <th class="text-right">Memory %</th>
            <th class="text-right">CPU</th>
            <th class="text-right">Memory</th>
            <th class="text-right">Disk</th>
            <th class="text-right">Object storage</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="c in rows" :key="c.customerId ?? 'none'">
            <td class="font-weight-medium">{{ c.customerName }}</td>
            <td class="text-right font-weight-bold">{{ pct(c.blendedPercentOfCluster) }}</td>
            <td class="text-right">{{ pct(c.cpuPercentOfCluster) }}</td>
            <td class="text-right">{{ pct(c.memoryPercentOfCluster) }}</td>
            <td class="text-right">{{ c.cpuPercent.toFixed(1) }}%</td>
            <td class="text-right">{{ formatBytes(c.memoryUsedBytes) }}</td>
            <td class="text-right">{{ formatBytes(c.diskBytes) }}</td>
            <td class="text-right">{{ formatBytes(c.objectStorageBytes) }}</td>
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
      </v-table>
    </v-card>

    <p class="text-caption text-medium-emphasis mt-3">
      <template v-if="isLive">
        Blended utilization is the mean of a customer's CPU share and memory share of total cluster
        capacity, measured at the latest poll. Pick a month above for the billable average.
      </template>
      <template v-else>
        Month figures are the average of {{ monthData?.sampleCount ?? 0 }} billing samples recorded
        across the month. A customer present for part of the month is averaged over the whole month,
        so partial months bill proportionally.
      </template>
    </p>
  </v-container>
</template>
