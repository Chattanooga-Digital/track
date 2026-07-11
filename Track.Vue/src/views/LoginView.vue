<script setup lang="ts">
import { ref } from 'vue'
import { useAuthStore } from '@/stores/authStore'

const authStore = useAuthStore()

const username = ref('')
const password = ref('')
const error = ref('')
const submitting = ref(false)

async function submit() {
  error.value = ''
  submitting.value = true
  try {
    await authStore.login(username.value, password.value)
  } catch (e) {
    if (
      typeof e === 'object' &&
      e !== null &&
      'response' in e &&
      (e as { response?: { status?: number } }).response?.status === 401
    ) {
      error.value = 'Invalid username or password.'
    } else {
      error.value = 'Login failed. Please try again.'
    }
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <v-container class="fill-height" fluid>
    <v-row justify="center" align="center">
      <v-col cols="12" sm="8" md="4">
        <v-card variant="outlined">
          <v-card-title class="text-center pt-6">
            <img src="/logo.svg" alt="Track" height="56" />
            <div class="text-h6 mt-2">Track</div>
            <div class="text-caption text-medium-emphasis">Docker Swarm monitoring</div>
          </v-card-title>
          <v-card-text>
            <v-form @submit.prevent="submit">
              <v-text-field
                v-model="username"
                label="Username"
                prepend-inner-icon="mdi-account"
                autofocus
                autocomplete="username"
              />
              <v-text-field
                v-model="password"
                label="Password"
                type="password"
                prepend-inner-icon="mdi-lock"
                autocomplete="current-password"
              />
              <v-alert v-if="error" type="error" density="compact" variant="tonal" class="mb-3">
                {{ error }}
              </v-alert>
              <v-btn
                type="submit"
                color="primary"
                block
                :loading="submitting"
                :disabled="!username || !password"
              >
                Sign In
              </v-btn>
            </v-form>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>
  </v-container>
</template>
