<script setup lang="ts">
import { ref, computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useSnapshotStore } from '@/stores/snapshotStore'
import type { CustomerGroupDto, StackGroupDto } from '@/types/stats'
import { formatBytes, formatCpu } from '@/utils/format'

const STACKS_SHOWN = 8

const snapshotStore = useSnapshotStore()
const { customers, nodes, snapshot } = storeToRefs(snapshotStore)

const cluster = computed(() => snapshot.value?.cluster)
const total = computed(() => snapshot.value?.totals)

// One scale for every card, so a bar in one customer compares with a bar in another.
const largestStackShare = computed(() =>
  Math.max(
    0,
    ...customers.value.flatMap((c) => c.stacks.map((s) => s.totals.blendedPercentOfCluster)),
  ),
)

function shareBar(share: number): number {
  return largestStackShare.value > 0 ? (share / largestStackShare.value) * 100 : 0
}

function customerKey(group: CustomerGroupDto): string {
  return `${group.customerId ?? (group.isShared ? 'shared' : 'unassigned')}`
}

function groupName(group: CustomerGroupDto): string {
  return group.customerName ?? (group.isShared ? 'Shared infrastructure' : 'Unassigned stacks')
}

function stackKey(group: CustomerGroupDto, stack: StackGroupDto): string {
  return `${customerKey(group)}::${stack.stackName ?? 'none'}`
}

const expandedStacks = ref<Set<string>>(new Set())
const fullyShownCustomers = ref<Set<string>>(new Set())

function toggleStack(key: string) {
  const next = new Set(expandedStacks.value)
  if (!next.delete(key)) next.add(key)
  expandedStacks.value = next
}

function showAllStacks(key: string) {
  fullyShownCustomers.value = new Set(fullyShownCustomers.value).add(key)
}

function stacksByShare(group: CustomerGroupDto): StackGroupDto[] {
  return [...group.stacks].sort(
    (a, b) => b.totals.blendedPercentOfCluster - a.totals.blendedPercentOfCluster,
  )
}

function visibleStacks(group: CustomerGroupDto): StackGroupDto[] {
  const sorted = stacksByShare(group)
  return fullyShownCustomers.value.has(customerKey(group)) ? sorted : sorted.slice(0, STACKS_SHOWN)
}

function hiddenStackCount(group: CustomerGroupDto): number {
  return fullyShownCustomers.value.has(customerKey(group))
    ? 0
    : Math.max(0, group.stacks.length - STACKS_SHOWN)
}

function plural(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? '' : 's'}`
}

function customerSummary(group: CustomerGroupDto): string {
  const parts = [plural(group.stacks.length, 'stack')]
  if (group.buckets.length > 0) parts.push(plural(group.buckets.length, 'bucket'))
  return parts.join(' · ')
}
</script>

<template>
  <div class="track-page">
    <div class="d-flex flex-wrap align-end justify-space-between ga-3 mb-6">
      <div>
        <h1 class="text-h5 font-weight-medium">Dashboard</h1>
        <div v-if="snapshot" class="text-body-2 text-medium-emphasis">
          Updated {{ new Date(snapshot.timestamp).toLocaleTimeString() }}
        </div>
      </div>
      <div class="d-flex flex-wrap ga-2">
        <v-chip
          v-for="node in nodes"
          :key="node.name"
          size="small"
          :color="node.online ? 'success' : 'error'"
          variant="tonal"
        >
          <v-icon start icon="mdi-server" />
          {{ node.name }}
        </v-chip>
      </div>
    </div>

    <div v-if="cluster && total" class="stat-grid mb-6">
      <v-card variant="flat" border class="pa-4">
        <div class="text-caption text-medium-emphasis">Blended cluster load</div>
        <div class="text-h5 font-weight-medium my-1">
          {{ total.blendedPercentOfCluster.toFixed(1) }}%
        </div>
        <v-progress-linear
          :model-value="total.blendedPercentOfCluster"
          color="primary"
          height="6"
          rounded
        />
        <div class="text-caption text-medium-emphasis mt-1">Mean of CPU and memory share</div>
      </v-card>
      <v-card variant="flat" border class="pa-4">
        <div class="text-caption text-medium-emphasis">CPU used</div>
        <div class="text-h5 font-weight-medium my-1">
          {{ total.cpuPercentOfCluster.toFixed(1) }}%
        </div>
        <v-progress-linear
          :model-value="total.cpuPercentOfCluster"
          color="primary"
          height="6"
          rounded
        />
        <div class="text-caption text-medium-emphasis mt-1">
          {{ (total.cpuPercent / 100).toFixed(2) }} of {{ cluster.cpuCores }} vCPU
        </div>
      </v-card>
      <v-card variant="flat" border class="pa-4">
        <div class="text-caption text-medium-emphasis">Memory used</div>
        <div class="text-h5 font-weight-medium my-1">
          {{ total.memoryPercentOfCluster.toFixed(1) }}%
        </div>
        <v-progress-linear
          :model-value="total.memoryPercentOfCluster"
          color="primary"
          height="6"
          rounded
        />
        <div class="text-caption text-medium-emphasis mt-1">
          {{ formatBytes(total.memoryUsedBytes) }} of {{ formatBytes(cluster.memoryBytes) }}
        </div>
      </v-card>
      <v-card variant="flat" border class="pa-4">
        <div class="text-caption text-medium-emphasis">Disk used</div>
        <div class="text-h5 font-weight-medium my-1">{{ formatBytes(total.diskBytes) }}</div>
        <div class="text-caption text-medium-emphasis mt-1">
          {{ plural(cluster.nodeCount, 'node')
          }}<template v-if="total.objectStorageBytes > 0">
            · plus {{ formatBytes(total.objectStorageBytes) }} object storage</template
          >
        </div>
      </v-card>
    </div>

    <v-alert v-if="customers.length === 0" type="info" variant="tonal">
      Waiting for the first poll to report container stats...
    </v-alert>

    <v-card v-for="group in customers" :key="customerKey(group)" variant="flat" border class="mb-5">
      <div class="d-flex flex-wrap align-center ga-4 px-5 py-4 row-border">
        <div class="d-flex flex-wrap align-center ga-3 customer-name">
          <h2 class="text-subtitle-1 font-weight-medium">{{ groupName(group) }}</h2>
          <span class="text-caption text-medium-emphasis">{{ customerSummary(group) }}</span>
          <v-chip v-if="group.isShared" size="x-small" variant="tonal">Not billed</v-chip>
          <v-chip
            v-else-if="group.customerId === null"
            size="x-small"
            color="warning"
            variant="tonal"
            >Not billed · assign to a customer or shared infrastructure</v-chip
          >
        </div>
        <dl class="d-flex flex-wrap ga-6">
          <div>
            <dt class="text-caption text-medium-emphasis">CPU</dt>
            <dd class="font-weight-medium">{{ formatCpu(group.totals.cpuPercent) }}</dd>
          </div>
          <div>
            <dt class="text-caption text-medium-emphasis">Memory</dt>
            <dd class="font-weight-medium">{{ formatBytes(group.totals.memoryUsedBytes) }}</dd>
          </div>
          <div>
            <dt class="text-caption text-medium-emphasis">Disk</dt>
            <dd class="font-weight-medium">{{ formatBytes(group.totals.diskBytes) }}</dd>
          </div>
          <div v-if="group.totals.objectStorageBytes > 0">
            <dt class="text-caption text-medium-emphasis">Object storage</dt>
            <dd class="font-weight-medium">{{ formatBytes(group.totals.objectStorageBytes) }}</dd>
          </div>
          <div>
            <dt class="text-caption text-medium-emphasis">Share of cluster</dt>
            <dd class="font-weight-medium text-primary">
              {{ group.totals.blendedPercentOfCluster.toFixed(1) }}%
            </dd>
          </div>
        </dl>
      </div>

      <div class="table-scroll">
        <div role="table" :aria-label="`${groupName(group)} stacks`">
          <div
            role="row"
            class="usage-grid header-row text-caption text-medium-emphasis row-border"
          >
            <div role="columnheader">Stack</div>
            <div role="columnheader" class="text-right">CPU</div>
            <div role="columnheader" class="text-right">Memory</div>
            <div role="columnheader" class="text-right">Disk</div>
            <div role="columnheader">Share of cluster</div>
          </div>

          <template v-for="stack in visibleStacks(group)" :key="stackKey(group, stack)">
            <div role="row" class="usage-grid stack-row row-border">
              <div role="cell" class="name-cell">
                <button
                  type="button"
                  class="stack-toggle"
                  :aria-expanded="expandedStacks.has(stackKey(group, stack))"
                  @click="toggleStack(stackKey(group, stack))"
                >
                  <v-icon
                    size="small"
                    class="text-medium-emphasis"
                    :icon="
                      expandedStacks.has(stackKey(group, stack))
                        ? 'mdi-chevron-down'
                        : 'mdi-chevron-right'
                    "
                  />
                  <span class="font-weight-medium text-truncate">{{
                    stack.stackName ?? 'No stack'
                  }}</span>
                </button>
                <span class="text-caption text-medium-emphasis text-no-wrap">{{
                  plural(stack.containers.length, 'container')
                }}</span>
              </div>
              <div role="cell" class="text-right">{{ formatCpu(stack.totals.cpuPercent) }}</div>
              <div role="cell" class="text-right">
                {{ formatBytes(stack.totals.memoryUsedBytes) }}
              </div>
              <div role="cell" class="text-right">{{ formatBytes(stack.totals.diskBytes) }}</div>
              <div role="cell" class="d-flex align-center ga-3">
                <v-progress-linear
                  :model-value="shareBar(stack.totals.blendedPercentOfCluster)"
                  color="primary"
                  height="6"
                  rounded
                />
                <span class="share-value"
                  >{{ stack.totals.blendedPercentOfCluster.toFixed(1) }}%</span
                >
              </div>
            </div>

            <div v-if="expandedStacks.has(stackKey(group, stack))" class="container-rows">
              <div
                v-for="c in stack.containers"
                :key="c.nodeName + c.containerId"
                role="row"
                class="usage-grid container-row row-border text-body-2"
              >
                <div role="cell" class="name-cell container-name">
                  <span class="text-truncate">{{ c.containerName }}</span>
                  <span class="text-caption text-medium-emphasis text-no-wrap">
                    {{ c.serviceName ? `${c.serviceName} · ` : '' }}{{ c.nodeName }}
                  </span>
                </div>
                <div role="cell" class="text-right">{{ formatCpu(c.cpuPercent) }}</div>
                <div role="cell" class="text-right">{{ formatBytes(c.memoryUsedBytes) }}</div>
                <div role="cell" class="text-right">{{ formatBytes(c.diskBytes) }}</div>
                <div role="cell"></div>
              </div>
            </div>
          </template>

          <div
            v-for="bucket in group.buckets"
            :key="`${bucket.sourceName}/${bucket.bucketName}`"
            role="row"
            class="usage-grid stack-row row-border"
          >
            <div role="cell" class="name-cell">
              <v-icon size="small" icon="mdi-database" class="text-medium-emphasis bucket-icon" />
              <span class="font-weight-medium text-truncate">{{ bucket.bucketName }}</span>
              <span class="text-caption text-medium-emphasis text-no-wrap">
                {{ bucket.sourceName
                }}<template v-if="bucket.objectCount > 0">
                  · {{ bucket.objectCount.toLocaleString() }} objects</template
                >
              </span>
            </div>
            <div role="cell"></div>
            <div role="cell"></div>
            <div role="cell" class="text-right">{{ formatBytes(bucket.sizeBytes) }}</div>
            <div role="cell" class="text-caption text-medium-emphasis">Object storage</div>
          </div>

          <div v-if="hiddenStackCount(group) > 0" class="px-5 py-3">
            <v-btn
              variant="outlined"
              size="small"
              color="primary"
              @click="showAllStacks(customerKey(group))"
              >Show {{ plural(hiddenStackCount(group), 'more stack') }}</v-btn
            >
          </div>
        </div>
      </div>
    </v-card>
  </div>
</template>

<style scoped>
.stat-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 16px;
}

.customer-name {
  flex: 1 1 260px;
}

dl,
dd {
  margin: 0;
}

.row-border {
  border-bottom: thin solid rgba(var(--v-border-color), var(--v-border-opacity));
}

.table-scroll {
  overflow-x: auto;
}

.table-scroll > [role='table'] {
  min-width: 720px;
}

.usage-grid {
  display: grid;
  grid-template-columns: minmax(240px, 1fr) 96px 150px 104px 180px;
  gap: 16px;
  align-items: center;
  padding: 0 20px;
}

.header-row {
  padding-top: 10px;
  padding-bottom: 10px;
}

.stack-row {
  min-height: 48px;
}

.container-rows {
  background: rgba(var(--v-theme-on-surface), 0.03);
}

.container-row {
  min-height: 40px;
}

.name-cell {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.container-name {
  align-items: baseline;
  padding-left: 32px;
}

.stack-toggle {
  display: flex;
  align-items: center;
  gap: 6px;
  min-height: 40px;
  min-width: 0;
  margin-left: -6px;
  padding: 0 6px;
  border: 0;
  border-radius: 6px;
  background: transparent;
  color: inherit;
  font: inherit;
  cursor: pointer;
}

.stack-toggle:hover {
  background: rgba(var(--v-theme-on-surface), 0.05);
}

.bucket-icon {
  width: 32px;
}

.share-value {
  width: 48px;
  text-align: right;
}
</style>
