import { http } from './http'
import type { BillingRateDto } from '@/types/stats'

export async function getBillingRates(): Promise<BillingRateDto> {
  const { data } = await http.get<BillingRateDto>('/billing/rates')
  return data
}

export async function updateBillingRates(model: BillingRateDto): Promise<BillingRateDto> {
  const { data } = await http.put<BillingRateDto>('/billing/rates', model)
  return data
}
