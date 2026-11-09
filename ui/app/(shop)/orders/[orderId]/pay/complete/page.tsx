import { notFound, redirect } from 'next/navigation'
import { PaymentComplete } from '@/components/Payment/paymentProgress'
import { apiFetch } from '@/lib/api'
import { formatUsd, isGuid, type OrderDetails } from '@/lib/orders'
import { getSession } from '@/lib/session'

// Where the card form goes once Stripe accepted the payment; also Stripe's return_url after a 3-D Secure redirect.
export default async function PaymentCompletePage({ params }: { params: Promise<{ orderId: string }> }) {
  const { orderId } = await params
  if (!isGuid(orderId)) notFound()

  const session = await getSession()
  if (!session) redirect('/autorization')

  const res = await apiFetch(`/api/v1/orders/${orderId}`)
  if (res.status === 404 || res.status === 403) notFound()
  const order = res.ok ? await res.json() as OrderDetails : null

  return (
    <div className='mx-auto w-full max-w-lg px-4 py-16'>
      <PaymentComplete orderId={orderId} amountLabel={order ? formatUsd(order.totalPrice) : 'your payment'} />
    </div>
  )
}
