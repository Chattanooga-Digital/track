<script setup lang="ts">
import { onMounted, watch } from 'vue'
import { useSnapshotStore } from '@/stores/snapshotStore'
import { useAuthStore } from '@/stores/authStore'
import LoginView from '@/views/LoginView.vue'

const snapshotStore = useSnapshotStore()
const authStore = useAuthStore()

onMounted(() => {
  authStore.initialize()
})

// Start the live feed only once authenticated; tear it down on logout.
watch(
  () => authStore.isAuthenticated,
  (authed) => {
    if (authed) {
      snapshotStore.start()
    } else {
      snapshotStore.stop()
    }
  },
)

async function logout() {
  await authStore.logout()
}
</script>

<template>
  <v-app>
    <template v-if="!authStore.ready">
      <v-main class="d-flex align-center justify-center">
        <v-progress-circular indeterminate />
      </v-main>
    </template>

    <template v-else-if="!authStore.isAuthenticated">
      <v-main>
        <LoginView />
      </v-main>
    </template>

    <template v-else>
      <v-app-bar density="compact" color="surface">
        <v-app-bar-title class="font-weight-bold">
          <img
            src="/logo.svg"
            alt="Track"
            height="24"
            class="mr-2"
            style="vertical-align: middle"
          />
          Track
        </v-app-bar-title>
        <template #append>
          <v-btn to="/" variant="text" size="small" prepend-icon="mdi-view-dashboard"
            >Dashboard</v-btn
          >
          <v-btn to="/billing" variant="text" size="small" prepend-icon="mdi-cash-multiple"
            >Billing</v-btn
          >
          <v-btn to="/customers" variant="text" size="small" prepend-icon="mdi-account-group"
            >Customers</v-btn
          >
          <v-btn to="/stacks" variant="text" size="small" prepend-icon="mdi-layers">Stacks</v-btn>
          <v-btn to="/agents" variant="text" size="small" prepend-icon="mdi-server-network"
            >Agents</v-btn
          >
          <v-btn to="/storage" variant="text" size="small" prepend-icon="mdi-database"
            >Storage</v-btn
          >
          <v-btn to="/unidentified" variant="text" size="small" prepend-icon="mdi-help-network"
            >Unidentified</v-btn
          >

          <v-chip
            size="small"
            :color="snapshotStore.connected ? 'success' : 'warning'"
            variant="flat"
            class="ml-3"
          >
            <v-icon
              start
              :icon="snapshotStore.connected ? 'mdi-lan-connect' : 'mdi-lan-disconnect'"
            />
            {{ snapshotStore.connected ? 'Live' : 'Reconnecting' }}
          </v-chip>

          <v-menu>
            <template #activator="{ props }">
              <v-btn icon="mdi-account-circle" variant="text" v-bind="props" class="ml-1" />
            </template>
            <v-list>
              <v-list-item>
                <v-list-item-title class="text-caption text-medium-emphasis">
                  {{ authStore.user?.username }}
                </v-list-item-title>
              </v-list-item>
              <v-divider />
              <v-list-item prepend-icon="mdi-logout" title="Sign out" @click="logout" />
            </v-list>
          </v-menu>
        </template>
      </v-app-bar>

      <v-main>
        <v-alert
          v-for="node in snapshotStore.offlineNodes"
          :key="node.name"
          type="error"
          variant="tonal"
          density="compact"
          class="ma-3 mb-0"
        >
          Node <strong>{{ node.name }}</strong> is unreachable: {{ node.error }}
        </v-alert>

        <router-view />
      </v-main>
    </template>
  </v-app>
</template>

<style>
html,
body,
#app {
  margin: 0;
}
</style>
