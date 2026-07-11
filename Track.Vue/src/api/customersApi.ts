import { http } from './http'
import type { BucketRefDto, CustomerDto, CustomerEditDto } from '@/types/customer'

export async function getCustomers(): Promise<CustomerDto[]> {
  const { data } = await http.get<CustomerDto[]>('/customers')
  return data
}

export async function createCustomer(model: CustomerEditDto): Promise<CustomerDto> {
  const { data } = await http.post<CustomerDto>('/customers', model)
  return data
}

export async function updateCustomer(id: number, model: CustomerEditDto): Promise<void> {
  await http.put(`/customers/${id}`, model)
}

export async function deleteCustomer(id: number): Promise<void> {
  await http.delete(`/customers/${id}`)
}

export async function setCustomerStacks(id: number, stackNames: string[]): Promise<void> {
  await http.put(`/customers/${id}/stacks`, stackNames)
}

export async function setCustomerBuckets(id: number, buckets: BucketRefDto[]): Promise<void> {
  await http.put(`/customers/${id}/buckets`, buckets)
}
