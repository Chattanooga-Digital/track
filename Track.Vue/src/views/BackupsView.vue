<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useSnapshotStore } from '@/stores/snapshotStore'
import type { BackupGroupDto, BackupOverviewDto, BackupVolumeDto } from '@/types/backups'
import {
  backUpAll,
  backUpVolume,
  cancelBackupJob,
  getBackupOverview,
  setVolumeOverride,
} from '@/api/backupsApi'
import { apiErrorMessage } from '@/api/http'
import { absoluteTime, formatBytes, relativeTime } from '@/utils/format'
import RestoreDialog from '@/components/RestoreDialog.vue'
import BackupJobsTable from '@/components/BackupJobsTable.vue'
import BackupSettingsPanel from '@/components/BackupSettingsPanel.vue'

const { backupsVersion } = storeToRefs(useSnapshotStore())

const tab = ref<'volumes' | 'jobs' | 'settings'>('volumes')
const overview = ref<BackupOverviewDto | null>(null)
const loading = ref(true)
const attentionOnly = ref(false)
const busyKey = ref<string | null>(null)
const message = ref<{ text: string; error: boolean } | null>(null)
const restoreVolume = ref<BackupVolumeDto | null>(null)

const configured = computed(() => !!overview.value?.targetSet && !!overview.value?.masterKeySet)

async function load() {
  try {
    overview.value = await getBackupOverview()
  } finally {
    loading.value = false
  }
}

onMounted(load)
watch(backupsVersion, load)

const needsAttention = (v: BackupVolumeDto) =>
  v.databaseWithoutHook ||
  ['Stale', 'Failed', 'StaleCopy', 'Ambiguous', 'NodeOffline'].includes(v.status)

const anonymousVolume = /^[0-9a-f]{64}$/

const displayName = (v: BackupVolumeDto) =>
  anonymousVolume.test(v.volumeName) ? v.volumeName.slice(0, 12) : v.volumeName

const groups = computed<BackupGroupDto[]>(() =>
  (overview.value?.groups ?? [])
    .map((g) => ({
      ...g,
      volumes: attentionOnly.value ? g.volumes.filter(needsAttention) : g.volumes,
    }))
    .filter((g) => g.volumes.length > 0),
)

function groupName(g: BackupGroupDto) {
  if (g.customerName) return g.customerName
  return g.isShared ? 'Shared infrastructure' : 'Unassigned stacks'
}

async function run(key: string, action: () => Promise<void>, done?: string) {
  busyKey.value = key
  message.value = null
  try {
    await action()
    if (done) message.value = { text: done, error: false }
    await load()
  } catch (e) {
    message.value = { text: apiErrorMessage(e, 'That didn’t work.'), error: true }
  } finally {
    busyKey.value = null
  }
}

const backUp = (v: BackupVolumeDto) => run(v.key, () => backUpVolume(v.key))
const cancel = (v: BackupVolumeDto) => run(v.key, () => cancelBackupJob(v.activeJobId!))
const setOverride = (v: BackupVolumeDto, excluded: boolean | null) =>
  run(v.key, () => setVolumeOverride(v.volumeName, excluded))
const backUpEverything = () => run('all', backUpAll, 'Backups queued.')

const isProtected = (v: BackupVolumeDto) =>
  !['Excluded', 'StaleCopy', 'Ambiguous'].includes(v.status)

interface StatusChip {
  text: string
  color?: string
  variant?: 'tonal' | 'outlined'
}

function statusChip(v: BackupVolumeDto): StatusChip {
  switch (v.status) {
    case 'Ok':
      return { text: 'OK', color: 'success' }
    case 'Queued':
      return { text: 'Queued', color: 'info' }
    case 'Running': {
      const verb = v.activeJobKind === 'Backup' ? 'Backing up' : 'Restoring'
      const percent = v.percentDone != null ? ` ${Math.round(v.percentDone * 100)}%` : ''
      return { text: `${verb}${percent}`, color: 'info' }
    }
    case 'Stale':
      return { text: 'Stale', color: 'warning' }
    case 'Failed':
      return { text: 'Failed', color: 'error' }
    case 'Never':
      return { text: 'Not backed up yet' }
    case 'Excluded':
      return { text: v.excludedBy === 'Track' ? 'Excluded' : 'Excluded by label' }
    case 'StaleCopy':
      return { text: 'Stale copy', color: 'warning', variant: 'outlined' }
    case 'Ambiguous':
      return { text: 'Several copies', color: 'warning' }
    case 'NodeOffline':
      return { text: 'Node offline', color: 'error' }
  }
  return { text: v.status }
}
</script>

<template>
  <div class="track-page">
    <div class="d-flex align-center mb-4 ga-3 flex-wrap">
      <h1 class="text-h5">Backups</h1>
      <v-chip v-if="overview?.paused" size="small" color="warning" variant="tonal">Paused</v-chip>
      <v-spacer />
      <v-btn
        variant="tonal"
        color="primary"
        prepend-icon="mdi-backup-restore"
        :disabled="!configured"
        :loading="busyKey === 'all'"
        @click="backUpEverything"
        >Back up all now</v-btn
      >
    </div>

    <v-alert
      v-if="overview && !configured"
      type="warning"
      variant="tonal"
      density="compact"
      class="mb-4"
    >
      {{ !overview.targetSet ? 'No backup target set.' : 'The master key isn’t set.' }}
      <template #append>
        <v-btn variant="text" size="small" @click="tab = 'settings'">Settings</v-btn>
      </template>
    </v-alert>

    <v-alert
      v-if="message"
      :type="message.error ? 'error' : 'success'"
      variant="tonal"
      density="compact"
      closable
      class="mb-4"
      @click:close="message = null"
    >
      {{ message.text }}
    </v-alert>

    <div class="tiles mb-4">
      <v-card variant="flat" border class="pa-4">
        <div class="text-caption text-medium-emphasis">Protected</div>
        <div class="text-h5">
          {{ overview?.protected ?? ''
          }}<span v-if="overview" class="text-medium-emphasis"> / {{ overview.included }}</span>
        </div>
      </v-card>
      <v-card variant="flat" border class="pa-4">
        <div class="text-caption text-medium-emphasis">Needs attention</div>
        <div class="text-h5" :class="{ 'text-warning': (overview?.needsAttention ?? 0) > 0 }">
          {{ overview?.needsAttention ?? '' }}
        </div>
      </v-card>
      <v-card variant="flat" border class="pa-4">
        <div class="text-caption text-medium-emphasis">Excluded</div>
        <div class="text-h5">{{ overview?.excluded ?? '' }}</div>
      </v-card>
      <v-card variant="flat" border class="pa-4">
        <div class="text-caption text-medium-emphasis">Uploaded, last 24 h</div>
        <div class="text-h5">{{ formatBytes(overview?.uploadedLast24HoursBytes ?? 0) }}</div>
      </v-card>
    </div>

    <div class="d-flex align-center flex-wrap ga-2 mb-3">
      <v-tabs v-model="tab" color="primary" density="comfortable">
        <v-tab value="volumes">Volumes</v-tab>
        <v-tab value="jobs">Jobs</v-tab>
        <v-tab value="settings">Settings</v-tab>
      </v-tabs>
      <v-spacer />
      <v-chip
        v-if="tab === 'volumes'"
        :color="attentionOnly ? 'primary' : undefined"
        :variant="attentionOnly ? 'tonal' : 'outlined'"
        size="small"
        @click="attentionOnly = !attentionOnly"
        >Needs attention</v-chip
      >
    </div>

    <template v-if="tab === 'volumes'">
      <v-progress-linear v-if="loading" indeterminate />
      <v-card variant="outlined">
        <div class="table-scroll">
          <v-table density="comfortable" class="volumes">
            <thead>
              <tr>
                <th>Volume</th>
                <th>Node</th>
                <th class="text-right">Size</th>
                <th class="text-no-wrap">Last good</th>
                <th>Status</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <template v-for="g in groups" :key="g.repositoryKey">
                <tr class="group-row">
                  <td colspan="6">
                    <span class="font-weight-medium">{{ groupName(g) }}</span>
                    <span class="text-caption text-medium-emphasis ml-2">{{
                      g.volumes.length
                    }}</span>
                    <span
                      v-if="g.repositorySizeBytes !== null"
                      class="text-caption text-medium-emphasis ml-3"
                      >{{ formatBytes(g.repositorySizeBytes) }} stored</span
                    >
                  </td>
                </tr>
                <tr
                  v-for="v in g.volumes"
                  :key="v.key"
                  :class="{ 'text-medium-emphasis': !isProtected(v) }"
                >
                  <td class="volume-cell">
                    <div class="d-flex align-center ga-2">
                      <span class="font-weight-medium text-truncate" :title="v.volumeName">{{
                        displayName(v)
                      }}</span>
                      <v-chip
                        v-if="v.databaseWithoutHook"
                        size="x-small"
                        color="warning"
                        variant="tonal"
                        prepend-icon="mdi-alert"
                        class="flex-shrink-0"
                        >No dump hook</v-chip
                      >
                      <v-chip
                        v-else-if="v.hasHooks"
                        size="x-small"
                        variant="tonal"
                        class="flex-shrink-0"
                        >Dump hook</v-chip
                      >
                    </div>
                    <div
                      v-if="v.paths.length"
                      class="text-caption text-medium-emphasis text-truncate"
                    >
                      Only {{ v.paths.join(', ') }}
                    </div>
                    <div
                      v-if="v.status === 'Failed' && v.lastError"
                      class="text-caption text-error text-truncate"
                      :title="v.lastError"
                    >
                      {{ v.lastError }}
                    </div>
                  </td>
                  <td class="text-no-wrap">{{ v.nodeName ?? v.copies.join(', ') }}</td>
                  <td class="text-right text-no-wrap">{{ formatBytes(v.sizeBytes) }}</td>
                  <td class="text-no-wrap" :title="absoluteTime(v.lastGoodAt)">
                    {{ v.lastGoodAt ? relativeTime(v.lastGoodAt) : '' }}
                  </td>
                  <td class="text-no-wrap">
                    <v-chip
                      size="small"
                      :color="statusChip(v).color"
                      :variant="statusChip(v).variant ?? 'tonal'"
                      >{{ statusChip(v).text }}</v-chip
                    >
                    <v-chip
                      v-if="v.override !== null"
                      size="x-small"
                      variant="outlined"
                      class="ml-1"
                      >Override</v-chip
                    >
                  </td>
                  <td class="text-right text-no-wrap">
                    <template v-if="isProtected(v)">
                      <v-btn
                        v-if="v.activeJobId"
                        v-tooltip:top="'Cancel'"
                        icon="mdi-close"
                        size="small"
                        variant="text"
                        :loading="busyKey === v.key"
                        @click="cancel(v)"
                      />
                      <v-btn
                        v-else
                        v-tooltip:top="'Back up now'"
                        icon="mdi-play"
                        size="small"
                        variant="text"
                        :disabled="!configured"
                        :loading="busyKey === v.key"
                        @click="backUp(v)"
                      />
                    </template>
                    <template v-if="v.status !== 'StaleCopy'">
                      <v-btn
                        v-tooltip:top="'Restore'"
                        icon="mdi-backup-restore"
                        size="small"
                        variant="text"
                        @click="restoreVolume = v"
                      />
                      <v-menu>
                        <template #activator="{ props }">
                          <v-btn
                            v-bind="props"
                            icon="mdi-dots-vertical"
                            size="small"
                            variant="text"
                          />
                        </template>
                        <v-list density="compact">
                          <v-list-item
                            v-if="v.status !== 'Excluded'"
                            title="Exclude"
                            @click="setOverride(v, true)"
                          />
                          <v-list-item v-else title="Include" @click="setOverride(v, false)" />
                          <v-list-item
                            v-if="v.override !== null"
                            title="Follow stack labels"
                            @click="setOverride(v, null)"
                          />
                        </v-list>
                      </v-menu>
                    </template>
                  </td>
                </tr>
              </template>
              <tr v-if="!loading && groups.length === 0">
                <td colspan="6" class="text-center text-medium-emphasis py-6">
                  {{ attentionOnly ? 'Nothing needs attention.' : 'No volumes yet.' }}
                </td>
              </tr>
            </tbody>
          </v-table>
        </div>
      </v-card>
    </template>

    <BackupJobsTable v-else-if="tab === 'jobs'" />
    <BackupSettingsPanel v-else />

    <RestoreDialog
      :volume="restoreVolume"
      :ready-nodes="overview?.readyNodes ?? []"
      @update:volume="restoreVolume = $event"
      @started="(text) => (message = { text, error: false })"
    />
  </div>
</template>

<style scoped>
.tiles {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 12px;
}

.table-scroll {
  overflow-x: auto;
}

.group-row td {
  background: rgba(var(--v-theme-on-surface), 0.04);
}

.volumes {
  min-width: 880px;
}

.volume-cell {
  width: 100%;
  max-width: 0;
}
</style>
