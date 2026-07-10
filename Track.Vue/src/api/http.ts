import axios from 'axios'

// In dev, Vite proxies /api and /hubs to the backend (see vite.config.ts).
// In production the SPA is served by the same host as the API, so a relative baseURL works.
export const http = axios.create({
  baseURL: '/api/v0',
})
