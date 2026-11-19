import { http } from './http'
import type {
  EmailSettingsDto,
  EmailSettingsEditDto,
  EmailTestDto,
  EmailTestResultDto,
} from '@/types/settings'

export async function getEmailSettings(): Promise<EmailSettingsDto> {
  const { data } = await http.get<EmailSettingsDto>('/settings/email')
  return data
}

export async function updateEmailSettings(model: EmailSettingsEditDto): Promise<void> {
  await http.put('/settings/email', model)
}

export async function testEmailSettings(model: EmailTestDto): Promise<EmailTestResultDto> {
  const { data } = await http.post<EmailTestResultDto>('/settings/email/test', model)
  return data
}
