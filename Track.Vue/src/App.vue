<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useDisplay } from 'vuetify'
import { useSnapshotStore } from '@/stores/snapshotStore'
import { useAuthStore } from '@/stores/authStore'
import LoginView from '@/views/LoginView.vue'
import ChangePasswordDialog from '@/components/ChangePasswordDialog.vue'
import { getAbout } from '@/api/aboutApi'

const snapshotStore = useSnapshotStore()
const authStore = useAuthStore()
const { mdAndUp } = useDisplay()
const changePasswordOpen = ref(false)
const drawerOpen = ref(false)
const version = ref('')

const unidentifiedCount = computed(() =>
  snapshotStore.nodes.reduce((sum, node) => sum + node.unidentifiedContainers, 0),
)

onMounted(async () => {
  authStore.initialize()
  version.value = (await getAbout()).version
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
      <v-app-bar v-if="!mdAndUp" density="compact" color="surface">
        <v-app-bar-nav-icon aria-label="Open navigation" @click="drawerOpen = !drawerOpen" />
        <v-app-bar-title class="font-weight-bold">Track</v-app-bar-title>
      </v-app-bar>

      <v-navigation-drawer
        v-model="drawerOpen"
        :permanent="mdAndUp"
        :temporary="!mdAndUp"
        width="240"
        color="surface"
      >
        <div class="d-flex align-center ga-2 px-5 pt-5 pb-2 text-subtitle-1 font-weight-bold">
          <img src="/logo.svg" alt="" height="24" />
          Track
        </div>

        <v-list nav density="comfortable">
          <v-list-subheader>Usage</v-list-subheader>
          <v-list-item to="/" exact prepend-icon="mdi-view-dashboard" title="Dashboard" />
          <v-list-item to="/billing" prepend-icon="mdi-cash-multiple" title="Billing" />

          <v-list-subheader>Setup</v-list-subheader>
          <v-list-item to="/customers" prepend-icon="mdi-account-group" title="Customers" />
          <v-list-item to="/stacks" prepend-icon="mdi-layers" title="Stacks" />
          <v-list-item to="/storage" prepend-icon="mdi-database" title="Storage" />
          <v-list-item to="/unidentified" prepend-icon="mdi-help-network" title="Unidentified">
            <template v-if="unidentifiedCount > 0" #append>
              <v-chip size="x-small" color="warning" variant="tonal">{{
                unidentifiedCount
              }}</v-chip>
            </template>
          </v-list-item>

          <v-list-subheader>Admin</v-list-subheader>
          <v-list-item to="/agents" prepend-icon="mdi-server-network" title="Agents" />
          <v-list-item
            v-if="authStore.user?.isAdministrator"
            to="/users"
            prepend-icon="mdi-account-multiple"
            title="Users"
          />
        </v-list>

        <template #append>
          <v-divider />
          <div class="pa-4 d-flex flex-column ga-3">
            <div class="d-flex align-center ga-2 text-body-2">
              <v-icon
                size="small"
                :color="snapshotStore.connected ? 'success' : 'warning'"
                :icon="snapshotStore.connected ? 'mdi-lan-connect' : 'mdi-lan-disconnect'"
              />
              {{ snapshotStore.connected ? 'Live' : 'Reconnecting' }}
            </div>
            <v-menu location="top">
              <template #activator="{ props }">
                <v-btn
                  v-bind="props"
                  variant="text"
                  size="small"
                  prepend-icon="mdi-account-circle"
                  class="justify-start px-1"
                  >{{ authStore.user?.username }}</v-btn
                >
              </template>
              <v-list density="compact">
                <v-list-item
                  prepend-icon="mdi-form-textbox-password"
                  title="Change password"
                  @click="changePasswordOpen = true"
                />
                <v-list-item prepend-icon="mdi-logout" title="Sign out" @click="logout" />
              </v-list>
            </v-menu>
            <div v-if="version" class="text-caption text-medium-emphasis">Track v{{ version }}</div>
          </div>
        </template>
      </v-navigation-drawer>

      <ChangePasswordDialog v-model="changePasswordOpen" />

      <v-main>
        <div v-if="snapshotStore.offlineNodes.length > 0" class="track-page pb-0">
          <v-alert
            v-for="node in snapshotStore.offlineNodes"
            :key="node.name"
            type="error"
            variant="tonal"
            density="compact"
            class="mb-2"
          >
            Node <strong>{{ node.name }}</strong> is unreachable: {{ node.error }}
          </v-alert>
        </div>

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

.track-page {
  max-width: 1168px;
  margin-inline: auto;
  padding: 32px 24px 48px;
  font-variant-numeric: tabular-nums;
}
</style>
