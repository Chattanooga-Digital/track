import axios from 'axios'

// In dev, Vite proxies /api and /hubs to the backend (see vite.config.ts).
// In production the SPA is served by the same host as the API, so a relative baseURL works.
export const http = axios.create({
  baseURL: '/api/v0',
})

/** Extracts the plain-text message our API returns in a 400 body, if any. */
export function apiErrorMessage(error: unknown, fallback: string): string {
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const data = (error as { response?: { data?: unknown } }).response?.data
    if (typeof data === 'string' && data.length > 0) return data
  }
  return fallback
}
