import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { CurrentUserDto } from '@/types/auth'
import { login as apiLogin, logout as apiLogout, getCurrentUser } from '@/api/authApi'

export const useAuthStore = defineStore('auth', () => {
  const user = ref<CurrentUserDto | null>(null)
  const ready = ref(false)

  const isAuthenticated = computed(() => user.value !== null)

  /** Resolve the current session once on app load. */
  async function initialize() {
    user.value = await getCurrentUser()
    ready.value = true
  }

  async function login(username: string, password: string) {
    user.value = await apiLogin({ username, password })
  }

  async function logout() {
    await apiLogout()
    user.value = null
  }

  return {
    user,
    ready,
    isAuthenticated,
    initialize,
    login,
    logout,
  }
})
