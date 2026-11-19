<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useSnapshotStore } from '@/stores/snapshotStore'
import type {
  BackupGroupDto,
  BackupMethod,
  BackupOverviewDto,
  BackupVolumeDto,
  DatabaseEngine,
} from '@/types/backups'
import {
  backUpAll,
  backUpVolume,
  cancelBackupJob,
  getBackupOverview,
  setVolumeMethod,
  setVolumeOverride,
  testRestore,
} from '@/api/backupsApi'
import { apiErrorMessage } from '@/api/http'
import { absoluteTime, formatBytes, relativeTime } from '@/utils/format'
import RestoreDialog from '@/components/RestoreDialog.vue'
import SnapshotBrowser from '@/components/SnapshotBrowser.vue'
import BackupJobsTable from '@/components/BackupJobsTable.vue'
import BackupSettingsPanel from '@/components/BackupSettingsPanel.vue'

const { backupsVersion } = storeToRefs(useSnapshotStore())

const tab = ref<'volumes' | 'ignored' | 'jobs' | 'settings'>('volumes')
const overview = ref<BackupOverviewDto | null>(null)
const loading = ref(true)
const attentionOnly = ref(false)
const busyKey = ref<string | null>(null)
const message = ref<{ text: string; error: boolean } | null>(null)
const restoreVolume = ref<BackupVolumeDto | null>(null)
const browseVolume = ref<BackupVolumeDto | null>(null)
const selected = ref<string[]>([])

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
watch(tab, () => (selected.value = []))

const isIgnored = (v: BackupVolumeDto) => v.status === 'Excluded'

const needsAttention = (v: BackupVolumeDto) =>
  v.databaseWithoutDump ||
  v.lastTest?.succeeded === false ||
  ['Stale', 'Failed', 'StaleCopy', 'Ambiguous', 'NodeOffline'].includes(v.status)

const groups = computed<BackupGroupDto[]>(() =>
  (overview.value?.groups ?? [])
    .map((g) => ({
      ...g,
      volumes: g.volumes.filter((v) =>
        tab.value === 'ignored'
          ? isIgnored(v)
          : !isIgnored(v) && (!attentionOnly.value || needsAttention(v)),
      ),
    }))
    .filter((g) => g.volumes.length > 0),
)

const visibleNames = computed(() => [
  ...new Set(groups.value.flatMap((g) => g.volumes.map((v) => v.volumeName))),
])
const allSelected = computed(
  () =>
    visibleNames.value.length > 0 && visibleNames.value.every((n) => selected.value.includes(n)),
)

function toggleAll() {
  selected.value = allSelected.value ? [] : [...visibleNames.value]
}

const multipleNodes = computed(
  () =>
    new Set(
      (overview.value?.groups ?? []).flatMap((g) =>
        g.volumes.flatMap((v) => (v.nodeName ? [v.nodeName] : v.copies)),
      ),
    ).size > 1,
)
const columns = computed(() => (multipleNodes.value ? 7 : 6))

const anonymousVolume = /^[0-9a-f]{64}$/

const displayName = (v: BackupVolumeDto) =>
  anonymousVolume.test(v.volumeName) ? v.volumeName.slice(0, 12) : v.volumeName

const engineNames: Record<DatabaseEngine, string> = {
  Postgres: 'Postgres',
  MariaDb: 'MariaDB',
  MySql: 'MySQL',
}

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
  run(v.key, () => setVolumeOverride([v.volumeName], excluded))
const setMethod = (v: BackupVolumeDto, method: BackupMethod | null) =>
  run(v.key, () => setVolumeMethod(v.volumeName, method))
const test = (v: BackupVolumeDto) =>
  run(v.key, () => testRestore(v.key), `Testing a restore of ${v.volumeName}.`)
const backUpEverything = () => run('all', backUpAll, 'Backups queued.')

async function setSelected(excluded: boolean) {
  const names = [...selected.value]
  await run('bulk', () => setVolumeOverride(names, excluded))
  selected.value = []
}

const isProtected = (v: BackupVolumeDto) =>
  !['Excluded', 'StaleCopy', 'Ambiguous'].includes(v.status)

const ignoredReason = (v: BackupVolumeDto) =>
  v.excludedBy === 'Empty' ? 'Empty' : v.excludedBy === 'Label' ? 'Ignored by label' : 'Ignored'

interface StatusChip {
  text: string
  color?: string
  variant?: 'tonal' | 'outlined'
}

const runningVerb: Record<string, string> = {
  Backup: 'Backing up',
  Restore: 'Restoring',
  Import: 'Importing',
  Verify: 'Testing',
}

function statusChip(v: BackupVolumeDto): StatusChip {
  switch (v.status) {
    case 'Ok':
      return { text: 'OK', color: 'success' }
    case 'Queued':
      return { text: 'Queued', color: 'info' }
    case 'Running': {
      const verb = runningVerb[v.activeJobKind ?? 'Backup'] ?? 'Running'
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
      return { text: ignoredReason(v) }
    case 'StaleCopy':
      return { text: 'Stale copy', color: 'warning', variant: 'outlined' }
    case 'Ambiguous':
      return { text: 'Several copies', color: 'warning' }
    case 'NodeOffline':
      return { text: 'Node offline', color: 'error' }
  }
  return { text: v.status }
}

function testTooltip(v: BackupVolumeDto) {
  const test = v.lastTest
  if (!test) return ''
  return [test.message, test.finishedAt ? relativeTime(test.finishedAt) : '']
    .filter(Boolean)
    .join(' · ')
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
      v-for="d in overview?.anonymousDatabases ?? []"
      :key="`${d.nodeName}/${d.serviceName}`"
      type="warning"
      variant="tonal"
      density="compact"
      class="mb-4"
    >
      {{ d.serviceName }} keeps its database in an anonymous volume, so a redeploy starts it empty.
      Name the volume in the stack.
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
        <div class="text-caption text-medium-emphasis">Ignored</div>
        <div class="text-h5">{{ overview?.ignored ?? '' }}</div>
      </v-card>
      <v-card variant="flat" border class="pa-4">
        <div class="text-caption text-medium-emphasis">Uploaded, last 24 h</div>
        <div class="text-h5">{{ formatBytes(overview?.uploadedLast24HoursBytes ?? 0) }}</div>
      </v-card>
    </div>

    <div class="d-flex align-center flex-wrap ga-2 mb-3">
      <v-tabs v-model="tab" color="primary" density="comfortable">
        <v-tab value="volumes">Volumes</v-tab>
        <v-tab value="ignored">Ignored</v-tab>
        <v-tab value="jobs">Jobs</v-tab>
        <v-tab value="settings">Settings</v-tab>
      </v-tabs>
      <v-spacer />
      <template v-if="selected.length > 0">
        <span class="text-body-2 text-medium-emphasis">{{ selected.length }} selected</span>
        <v-btn
          v-if="tab === 'volumes'"
          size="small"
          variant="tonal"
          :loading="busyKey === 'bulk'"
          @click="setSelected(true)"
          >Ignore</v-btn
        >
        <v-btn
          v-else
          size="small"
          variant="tonal"
          color="primary"
          :loading="busyKey === 'bulk'"
          @click="setSelected(false)"
          >Back up</v-btn
        >
        <v-btn size="small" variant="text" @click="selected = []">Clear</v-btn>
      </template>
      <v-chip
        v-else-if="tab === 'volumes'"
        :color="attentionOnly ? 'primary' : undefined"
        :variant="attentionOnly ? 'tonal' : 'outlined'"
        size="small"
        @click="attentionOnly = !attentionOnly"
        >Needs attention</v-chip
      >
    </div>

    <template v-if="tab === 'volumes' || tab === 'ignored'">
      <v-progress-linear v-if="loading" indeterminate />
      <v-card variant="outlined">
        <div class="table-scroll">
          <v-table density="comfortable" class="volumes">
            <thead>
              <tr>
                <th class="check-cell">
                  <v-checkbox-btn
                    :model-value="allSelected"
                    :indeterminate="selected.length > 0 && !allSelected"
                    density="compact"
                    @update:model-value="toggleAll"
                  />
                </th>
                <th>Volume</th>
                <th v-if="multipleNodes">Node</th>
                <th class="text-right">Size</th>
                <th class="text-no-wrap">Last good</th>
                <th>Status</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <template v-for="g in groups" :key="g.repositoryKey">
                <tr class="group-row">
                  <td :colspan="columns + 1">
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
                  <td class="check-cell">
                    <v-checkbox-btn v-model="selected" :value="v.volumeName" density="compact" />
                  </td>
                  <td class="volume-cell">
                    <div class="d-flex align-center ga-2">
                      <span class="font-weight-medium text-truncate" :title="v.volumeName">{{
                        displayName(v)
                      }}</span>
                      <v-chip
                        v-if="v.method === 'Dump' && v.engine"
                        size="x-small"
                        variant="tonal"
                        class="flex-shrink-0"
                        >{{ engineNames[v.engine] }} dump</v-chip
                      >
                      <v-chip
                        v-else-if="v.databaseWithoutDump"
                        v-tooltip:top="'Database files copied while it runs'"
                        size="x-small"
                        color="warning"
                        variant="tonal"
                        prepend-icon="mdi-alert"
                        class="flex-shrink-0"
                        >No dump</v-chip
                      >
                      <v-chip
                        v-else-if="v.hasHooks"
                        size="x-small"
                        variant="tonal"
                        class="flex-shrink-0"
                        >Dump hook</v-chip
                      >
                      <v-chip
                        v-if="v.lastTest"
                        v-tooltip:top="testTooltip(v)"
                        size="x-small"
                        :color="v.lastTest.succeeded ? 'success' : 'error'"
                        variant="tonal"
                        class="flex-shrink-0"
                        >{{ v.lastTest.succeeded ? 'Tested' : 'Test failed' }}</v-chip
                      >
                    </div>
                    <div
                      v-if="v.paths.length && v.method === 'Files'"
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
                  <td v-if="multipleNodes" class="text-no-wrap">
                    {{ v.nodeName ?? v.copies.join(', ') }}
                  </td>
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
                      v-if="tab === 'volumes' && (v.override !== null || v.methodOverride !== null)"
                      size="x-small"
                      variant="outlined"
                      class="ml-1"
                      >Override</v-chip
                    >
                  </td>
                  <td class="text-right text-no-wrap">
                    <v-btn
                      v-if="tab === 'ignored'"
                      v-tooltip:top="'Back up'"
                      icon="mdi-plus-circle-outline"
                      size="small"
                      variant="text"
                      :loading="busyKey === v.key"
                      @click="setOverride(v, false)"
                    />
                    <template v-else-if="isProtected(v)">
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
                        v-tooltip:top="'Browse'"
                        icon="mdi-folder-search-outline"
                        size="small"
                        variant="text"
                        @click="browseVolume = v"
                      />
                      <v-btn
                        v-if="tab === 'volumes'"
                        v-tooltip:top="'Restore'"
                        icon="mdi-backup-restore"
                        size="small"
                        variant="text"
                        @click="restoreVolume = v"
                      />
                      <v-menu
                        v-if="tab === 'volumes' || v.override !== null || v.methodOverride !== null"
                      >
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
                            v-if="v.method === 'Dump' && tab === 'volumes'"
                            title="Test restore"
                            :disabled="!configured || !!v.activeJobId"
                            @click="test(v)"
                          />
                          <template v-if="v.engine && tab === 'volumes'">
                            <v-list-item
                              title="Dump the database"
                              :append-icon="v.method === 'Dump' ? 'mdi-check' : undefined"
                              @click="setMethod(v, 'Dump')"
                            />
                            <v-list-item
                              title="Copy files"
                              :append-icon="v.method === 'Files' ? 'mdi-check' : undefined"
                              @click="setMethod(v, 'Files')"
                            />
                          </template>
                          <v-list-item
                            v-if="tab === 'volumes'"
                            title="Ignore"
                            @click="setOverride(v, true)"
                          />
                          <v-list-item
                            v-if="v.override !== null || v.methodOverride !== null"
                            title="Reset to default"
                            @click="
                              v.methodOverride !== null
                                ? run(v.key, async () => {
                                    await setVolumeMethod(v.volumeName, null)
                                    await setVolumeOverride([v.volumeName], null)
                                  })
                                : setOverride(v, null)
                            "
                          />
                        </v-list>
                      </v-menu>
                    </template>
                  </td>
                </tr>
              </template>
              <tr v-if="!loading && groups.length === 0">
                <td :colspan="columns + 1" class="text-center text-medium-emphasis py-6">
                  {{
                    tab === 'ignored'
                      ? 'Nothing ignored.'
                      : attentionOnly
                        ? 'Nothing needs attention.'
                        : 'No volumes yet.'
                  }}
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
    <SnapshotBrowser :volume="browseVolume" @update:volume="browseVolume = $event" />
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

.check-cell {
  width: 40px;
  padding-inline-end: 0 !important;
}

.volume-cell {
  width: 100%;
  max-width: 0;
}
</style>
