<script setup lang="ts">
import { ref, onMounted } from 'vue'
import type { UnidentifiedContainerDto } from '@/types/inventory'
import { getInventory } from '@/api/inventoryApi'
import { getIdentityEnvVars, setIdentityEnvVars } from '@/api/groupingApi'

const inventory = ref<UnidentifiedContainerDto[]>([])
const identityEnvVars = ref<string[]>([])
const loading = ref(true)
const saving = ref(false)
const newVar = ref('')
const snackbar = ref('')

async function refresh() {
  loading.value = true
  try {
    const [inv, ids] = await Promise.all([getInventory(), getIdentityEnvVars()])
    inventory.value = inv
    identityEnvVars.value = ids
  } finally {
    loading.value = false
  }
}

async function save(names: string[]) {
  saving.value = true
  try {
    identityEnvVars.value = await setIdentityEnvVars(names)
    snackbar.value = 'Saved. Containers regroup on the next agent poll.'
    inventory.value = await getInventory()
  } finally {
    saving.value = false
  }
}

function addVar(name: string) {
  const trimmed = name.trim()
  if (!trimmed || identityEnvVars.value.includes(trimmed)) return
  save([...identityEnvVars.value, trimmed])
  newVar.value = ''
}

function removeVar(name: string) {
  save(identityEnvVars.value.filter((n) => n !== name))
}

function isIdentity(key: string): boolean {
  return identityEnvVars.value.includes(key)
}

onMounted(refresh)
</script>

<template>
  <div class="track-page">
    <div class="d-flex align-center mb-4 ga-4 flex-wrap">
      <h1 class="text-h5">Unidentified containers</h1>
      <v-spacer />
      <v-btn
        variant="text"
        size="small"
        prepend-icon="mdi-refresh"
        :loading="loading"
        @click="refresh"
      >
        Refresh
      </v-btn>
    </div>

    <v-alert type="info" variant="tonal" density="compact" class="mb-4">
      Standalone containers with no Swarm or Compose stack label are grouped by the value of the
      first matching environment variable below. Pick an identity variable (e.g.
      <code>DISCOURSE_HOSTNAME</code>) and its value becomes a stack you can assign to a customer.
      Only the chosen variable's value is sent to Track; other env values never leave the node.
      Secret-looking variables (names containing <code>password</code>, <code>secret</code>,
      <code>token</code>, and similar) are hidden and cannot be used as an identity.
    </v-alert>

    <v-card variant="outlined" class="mb-4">
      <v-card-title class="text-subtitle-1">Grouping identity variables</v-card-title>
      <v-card-text>
        <div v-if="identityEnvVars.length" class="d-flex flex-wrap ga-2 mb-3">
          <v-chip
            v-for="(name, i) in identityEnvVars"
            :key="name"
            color="primary"
            closable
            :disabled="saving"
            @click:close="removeVar(name)"
          >
            <span class="text-caption text-medium-emphasis mr-1">{{ i + 1 }}.</span>{{ name }}
          </v-chip>
        </div>
        <div v-else class="text-medium-emphasis mb-3">
          No identity variables yet. Add one below, or click an env var chip on a container.
        </div>

        <v-text-field
          v-model="newVar"
          label="Add an environment variable name"
          placeholder="DISCOURSE_HOSTNAME"
          density="compact"
          hide-details
          :disabled="saving"
          style="max-width: 420px"
          @keyup.enter="addVar(newVar)"
        >
          <template #append>
            <v-btn variant="tonal" :disabled="!newVar.trim() || saving" @click="addVar(newVar)"
              >Add</v-btn
            >
          </template>
        </v-text-field>
      </v-card-text>
    </v-card>

    <v-card variant="outlined">
      <v-card-title class="text-subtitle-1">
        Containers awaiting an identity
        <v-chip size="x-small" class="ml-2">{{ inventory.length }}</v-chip>
      </v-card-title>

      <v-progress-linear v-if="loading" indeterminate />

      <v-card-text v-if="!loading && inventory.length === 0" class="text-medium-emphasis">
        No unidentified containers. Everything is grouped by a stack label or an identity variable.
      </v-card-text>

      <v-table v-else density="compact">
        <thead>
          <tr>
            <th>Node</th>
            <th>Container</th>
            <th>Image</th>
            <th>Environment variables (click to use as identity)</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="c in inventory" :key="c.nodeName + c.containerId">
            <td class="text-medium-emphasis">{{ c.nodeName }}</td>
            <td class="text-truncate" style="max-width: 200px">{{ c.containerName }}</td>
            <td class="text-medium-emphasis text-truncate" style="max-width: 220px">
              {{ c.image ?? '—' }}
            </td>
            <td>
              <div class="d-flex flex-wrap ga-1 py-1">
                <v-chip
                  v-for="key in c.envKeys"
                  :key="key"
                  size="x-small"
                  :color="isIdentity(key) ? 'primary' : undefined"
                  :variant="isIdentity(key) ? 'flat' : 'outlined'"
                  :disabled="saving || isIdentity(key)"
                  @click="addVar(key)"
                >
                  {{ key }}
                </v-chip>
                <span v-if="c.envKeys.length === 0" class="text-medium-emphasis text-caption">
                  no environment variables
                </span>
              </div>
            </td>
          </tr>
        </tbody>
      </v-table>
    </v-card>

    <v-snackbar
      :model-value="!!snackbar"
      :timeout="4000"
      color="success"
      @update:model-value="snackbar = ''"
    >
      {{ snackbar }}
    </v-snackbar>
  </div>
</template>
