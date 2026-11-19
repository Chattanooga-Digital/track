<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { BackupSnapshotDto, BackupVolumeDto, SnapshotListingDto } from '@/types/backups'
import { getSnapshotFiles, getSnapshots, snapshotDownloadUrl } from '@/api/backupsApi'
import { apiErrorMessage } from '@/api/http'
import { formatBytes } from '@/utils/format'

const props = defineProps<{ volume: BackupVolumeDto | null }>()

const emit = defineEmits<{ 'update:volume': [value: BackupVolumeDto | null] }>()

const snapshots = ref<BackupSnapshotDto[]>([])
const snapshotId = ref<number | null>(null)
const path = ref('')
const listing = ref<SnapshotListingDto | null>(null)
const loading = ref(false)
const error = ref('')

const open = computed({
  get: () => props.volume !== null,
  set: (value) => {
    if (!value) emit('update:volume', null)
  },
})

watch(
  () => props.volume,
  async (volume) => {
    if (!volume) return
    snapshots.value = []
    snapshotId.value = null
    listing.value = null
    error.value = ''
    loading.value = true
    try {
      snapshots.value = await getSnapshots(volume.volumeName)
      snapshotId.value = snapshots.value[0]?.id ?? null
    } finally {
      loading.value = false
    }
  },
)

watch(snapshotId, () => {
  if (path.value === '') void list()
  else path.value = ''
})

watch(path, () => void list())

async function list() {
  if (snapshotId.value === null) return
  loading.value = true
  error.value = ''
  try {
    listing.value = await getSnapshotFiles(snapshotId.value, path.value)
  } catch (e) {
    listing.value = null
    error.value = apiErrorMessage(e, 'Couldn’t read the snapshot.')
  } finally {
    loading.value = false
  }
}

const snapshotItems = computed(() =>
  snapshots.value.map((s) => ({
    value: s.id,
    title: [
      new Date(s.time).toLocaleString(),
      s.totalBytes !== null ? formatBytes(s.totalBytes) : '',
      s.dumpEngine ? 'Dump' : '',
      s.preRestore ? 'Before restore' : '',
    ]
      .filter(Boolean)
      .join(' · '),
  })),
)

const crumbs = computed(() => {
  const parts = path.value ? path.value.split('/') : []
  return [
    { title: props.volume?.volumeName ?? '', path: '' },
    ...parts.map((part, i) => ({ title: part, path: parts.slice(0, i + 1).join('/') })),
  ]
})

const childPath = (name: string) => (path.value ? `${path.value}/${name}` : name)

const modified = (value: string | null) => (value ? new Date(value).toLocaleString() : '')
</script>

<template>
  <v-dialog v-model="open" max-width="820">
    <v-card v-if="volume">
      <v-card-title class="d-flex align-center">
        <span class="text-truncate">Browse {{ volume.volumeName }}</span>
        <v-spacer />
        <v-btn icon="mdi-close" variant="text" size="small" @click="open = false" />
      </v-card-title>

      <v-card-text>
        <div v-if="!loading && snapshots.length === 0" class="text-medium-emphasis py-3">
          No snapshots yet.
        </div>
        <template v-else>
          <div class="d-flex align-center ga-2 mb-2">
            <v-select
              v-model="snapshotId"
              :items="snapshotItems"
              label="Snapshot"
              density="comfortable"
              variant="outlined"
              hide-details
            />
            <v-btn
              v-tooltip:top="path ? 'Download this folder' : 'Download the snapshot'"
              :href="snapshotId !== null ? snapshotDownloadUrl(snapshotId, path, true) : undefined"
              :disabled="snapshotId === null"
              icon="mdi-download"
              variant="tonal"
            />
          </div>

          <div class="d-flex align-center flex-wrap text-body-2 mb-1">
            <template v-for="(crumb, i) in crumbs" :key="crumb.path">
              <span v-if="i > 0" class="text-medium-emphasis mx-1">/</span>
              <a
                v-if="i < crumbs.length - 1"
                href="#"
                class="crumb"
                @click.prevent="path = crumb.path"
                >{{ crumb.title }}</a
              >
              <span v-else class="font-weight-medium">{{ crumb.title }}</span>
            </template>
          </div>

          <v-progress-linear v-if="loading" indeterminate class="mb-1" />
          <v-alert v-if="error" type="error" variant="tonal" density="compact" class="mb-2">
            {{ error }}
          </v-alert>

          <div class="listing">
            <v-table density="compact">
              <tbody>
                <tr v-for="entry in listing?.entries ?? []" :key="entry.name">
                  <td class="name-cell">
                    <v-icon
                      size="small"
                      class="mr-2"
                      :icon="entry.isDirectory ? 'mdi-folder-outline' : 'mdi-file-outline'"
                    />
                    <a
                      v-if="entry.isDirectory"
                      href="#"
                      class="crumb"
                      @click.prevent="path = childPath(entry.name)"
                      >{{ entry.name }}</a
                    >
                    <span v-else>{{ entry.name }}</span>
                  </td>
                  <td class="text-right text-no-wrap">
                    {{ entry.isDirectory ? '' : formatBytes(entry.size) }}
                  </td>
                  <td class="text-no-wrap text-medium-emphasis">
                    {{ modified(entry.modifiedAt) }}
                  </td>
                  <td class="text-right">
                    <v-btn
                      v-tooltip:top="'Download'"
                      :href="
                        snapshotId !== null
                          ? snapshotDownloadUrl(
                              snapshotId,
                              childPath(entry.name),
                              entry.isDirectory,
                            )
                          : undefined
                      "
                      icon="mdi-download"
                      size="small"
                      variant="text"
                    />
                  </td>
                </tr>
                <tr v-if="!loading && listing && listing.entries.length === 0">
                  <td colspan="4" class="text-center text-medium-emphasis py-4">Empty folder.</td>
                </tr>
              </tbody>
            </v-table>
          </div>
        </template>
      </v-card-text>
    </v-card>
  </v-dialog>
</template>

<style scoped>
.listing {
  max-height: 55vh;
  overflow: auto;
}

.name-cell {
  width: 100%;
  word-break: break-all;
}

.crumb {
  color: rgb(var(--v-theme-primary));
  text-decoration: none;
}
</style>
