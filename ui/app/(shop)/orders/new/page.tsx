import { redirect } from 'next/navigation'
import { FindingOrder } from '@/components/Payment/paymentProgress'
import { getSession } from '@/lib/session'

// The cart's checkout lands here: the order is created from a message a moment later, then its payment page opens.
export default async function NewOrderPage({ searchParams }: { searchParams: Promise<{ since?: string | string[] }> }) {
  const session = await getSession()
  if (!session) redirect('/autorization')

  const { since } = await searchParams

  return (
    <div className='mx-auto w-full max-w-lg px-4 py-16'>
      <FindingOrder since={typeof since === 'string' ? since : ''} />
    </div>
  )
}
