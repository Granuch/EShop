import { CheckCircle2, ShieldCheck, XCircle } from 'lucide-react'
import Link from 'next/link'
import { notFound, redirect } from 'next/navigation'
import { cn } from 'cn'
import { CancelOrderButton } from '@/components/Payment/cancelOrderButton'
import { CardPayment } from '@/components/Payment/cardPayment'
import { buttonVariants } from '@/components/ui/button'
import { apiFetch } from '@/lib/api'
import { formatUsd, isGuid, type OrderDetails } from '@/lib/orders'
import { getSession } from '@/lib/session'

export default async function PayOrderPage({ params }: { params: Promise<{ orderId: string }> }) {
  const { orderId } = await params
  if (!isGuid(orderId)) notFound()

  const session = await getSession()
  if (!session) redirect('/autorization')

  const res = await apiFetch(`/api/v1/orders/${orderId}`)
  if (res.status === 404 || res.status === 403) notFound()
  if (!res.ok) throw new Error(`The order could not be loaded (${res.status})`)
  const order = await res.json() as OrderDetails

  // Read at request time, so one image serves any Stripe account. The publishable key is public by design.
  const publishableKey = process.env.STRIPE_PUBLISHABLE_KEY ?? ''
  const amount = formatUsd(order.totalPrice)

  return (
    <div className='mx-auto grid w-full max-w-5xl gap-8 px-4 py-10 md:grid-cols-[1fr_1.1fr]'>
      <section className='flex min-w-0 flex-col gap-6'>
        <div>
          <p className='text-sm text-muted-foreground'>Order #{order.id.slice(0, 8)}</p>
          <h1 className='text-3xl font-semibold tracking-tight'>Checkout</h1>
        </div>
        <div className='flex flex-col gap-4 rounded-2xl border bg-card p-6 shadow-sm'>
          <h2 className='font-semibold'>Order summary</h2>
          <ul className='flex flex-col gap-3'>
            {order.items.map((item) => (
              <li key={item.id} className='flex items-baseline justify-between gap-4 text-sm'>
                <span className='min-w-0'>
                  <Link href={`/product/${item.productId}`} className='hover:underline'>{item.productName}</Link>
                  <span className='text-muted-foreground'> × {item.quantity}</span>
                </span>
                <span className='shrink-0 tabular-nums'>{formatUsd(item.unitPrice * item.quantity)}</span>
              </li>
            ))}
          </ul>
          <div className='flex items-baseline justify-between border-t pt-4'>
            <span className='text-muted-foreground'>Total</span>
            <span className='text-2xl font-bold tabular-nums'>{amount}</span>
          </div>
        </div>
        <div className='rounded-2xl border p-6 text-sm'>
          <h2 className='mb-2 font-semibold'>Shipping to</h2>
          <p className='text-muted-foreground'>
            {order.shippingAddress.street}<br />
            {order.shippingAddress.city}, {order.shippingAddress.state} {order.shippingAddress.zipCode}<br />
            {order.shippingAddress.country}
          </p>
        </div>
      </section>

      <section className='flex min-w-0 flex-col gap-4'>
        <h2 className='flex items-center gap-2 text-xl font-semibold'><ShieldCheck className='size-5' /> Payment</h2>
        {order.status === 'Pending' ? (
          publishableKey ? (
            <>
              {publishableKey.startsWith('pk_test_') && (
                <div className='rounded-xl border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-900'>
                  <p className='font-medium'>Test mode: no real money is charged.</p>
                  <p>Card <span className='font-mono'>4242 4242 4242 4242</span>, any future date, any CVC and ZIP.
                    A declined card: <span className='font-mono'>4000 0000 0000 0002</span>.</p>
                </div>
              )}
              <div className='rounded-2xl border bg-card p-6 shadow-sm'>
                <CardPayment orderId={order.id} publishableKey={publishableKey} amountLabel={amount} />
              </div>
              <div className='flex items-center justify-between gap-3 text-sm text-muted-foreground'>
                <span>Changed your mind? Cancel before paying and nothing is charged.</span>
                <CancelOrderButton orderId={order.id} redirectTo='/orders' />
              </div>
            </>
          ) : (
            <p role='alert' className='rounded-xl border p-6 text-red-600'>
              Card payments are not configured in this shop (STRIPE_PUBLISHABLE_KEY is not set).
            </p>
          )
        ) : order.status === 'Cancelled' ? (
          <div className='flex flex-col items-start gap-4 rounded-2xl border p-6'>
            <p className='flex items-center gap-2 font-medium'><XCircle className='size-5 text-red-600' /> This order was cancelled.</p>
            {order.cancellationReason && <p className='text-sm text-muted-foreground'>{order.cancellationReason}</p>}
            <Link href='/orders' className={cn(buttonVariants({ variant: 'outline' }))}>Back to my orders</Link>
          </div>
        ) : (
          <div className='flex flex-col items-start gap-4 rounded-2xl border p-6'>
            <p className='flex items-center gap-2 font-medium'><CheckCircle2 className='size-5 text-green-600' />
              {order.status === 'Paid' ? 'This order is already paid.' : `This order is already paid and ${order.status.toLowerCase()}.`}
            </p>
            <Link href='/orders' className={cn(buttonVariants({ variant: 'outline' }))}>Back to my orders</Link>
          </div>
        )}
      </section>
    </div>
  )
}
