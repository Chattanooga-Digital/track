import { http } from './http'
import type { CurrentUserDto, LoginDto } from '@/types/auth'

export async function login(model: LoginDto): Promise<CurrentUserDto> {
  const { data } = await http.post<CurrentUserDto>('/auth/login', model)
  return data
}

export async function logout(): Promise<void> {
  await http.post('/auth/logout')
}

/** Returns the current user, or null if not authenticated (401). */
export async function getCurrentUser(): Promise<CurrentUserDto | null> {
  try {
    const { data } = await http.get<CurrentUserDto>('/auth/me')
    return data
  } catch (error) {
    if (isUnauthorized(error)) return null
    throw error
  }
}

function isUnauthorized(error: unknown): boolean {
  return (
    typeof error === 'object' &&
    error !== null &&
    'response' in error &&
    (error as { response?: { status?: number } }).response?.status === 401
  )
}
