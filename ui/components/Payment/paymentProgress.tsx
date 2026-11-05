'use client'
import { CheckCircle2, Clock, Loader2, XCircle } from 'lucide-react'
import Link from 'next/link'
import { useRouter } from 'next/navigation'
import { useEffect, useState, type ReactNode } from 'react'
import { cn } from 'cn'
import { buttonVariants } from '@/components/ui/button'
import { findNewOrder, getPaymentProgress, type PaymentProgress } from '@/lib/payments'

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms))

/** After checkout: waits for Ordering to create the order (it arrives by message), then opens its payment page. */
export function FindingOrder({ since }: { since: string }) {
  const router = useRouter()
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    let cancelled = false
    async function run() {
      for (let i = 0; i < 30; i++) {
        const { orderId, signedOut } = await findNewOrder(since)
        if (cancelled) return
        if (signedOut) { router.replace('/autorization'); return }
        if (orderId) { router.replace(`/orders/${orderId}/pay`); return }
        await sleep(1500)
      }
      if (!cancelled) setFailed(true)
    }
    run()
    return () => { cancelled = true }
  }, [since, router])

  if (failed) {
    return (
      <Panel icon={<Clock className='size-10 text-gray-500' />} title='Your order is still being created'>
        <p className='text-gray-600'>It is taking longer than usual. It will appear in My orders, with a Pay button, shortly.</p>
        <Link href='/orders' className={cn(buttonVariants({ variant: 'outline' }))}>Go to my orders</Link>
      </Panel>
    )
  }
  return (
    <Panel icon={<Loader2 className='size-10 animate-spin text-gray-500' />} title='Creating your order…'>
      <p className='text-gray-600'>This takes a few seconds. The payment form opens next.</p>
    </Panel>
  )
}

type View = 'waiting' | 'paid' | 'confirming' | 'failed' | 'slow'

/**
 * After Stripe accepted the card: Stripe's webhook turns the payment Success within seconds, and the order turns
 * Paid from a message about 14 s later. The payment is what says the money was taken, so it decides the view.
 */
export function PaymentComplete({ orderId, amountLabel }: { orderId: string; amountLabel: string }) {
  const [view, setView] = useState<View>('waiting')
  const [progress, setProgress] = useState<PaymentProgress | null>(null)

  useEffect(() => {
    let cancelled = false
    async function run() {
      for (let i = 0; i < 40; i++) {
        const p = await getPaymentProgress(orderId)
        if (cancelled) return
        setProgress(p)
        if (p.paymentStatus === 'Success' || p.paymentStatus === 'Refunded' || p.orderStatus === 'Paid' || p.orderStatus === 'Shipped' || p.orderStatus === 'Delivered') {
          // Keep polling a little longer so the order's own status catches up, then stop.
          setView(p.orderStatus === 'Paid' || p.orderStatus === 'Shipped' || p.orderStatus === 'Delivered' ? 'paid' : 'confirming')
          if (p.orderStatus !== 'Pending') return
        }
        else if (p.paymentStatus === 'Failed' || p.paymentStatus === 'Cancelled' || p.orderStatus === 'Cancelled') {
          setView('failed')
          return
        }
        await sleep(2000)
      }
      if (!cancelled) setView((v) => (v === 'waiting' ? 'slow' : v))
    }
    run()
    return () => { cancelled = true }
  }, [orderId])

  if (view === 'paid' || view === 'confirming') {
    return (
      <Panel icon={<CheckCircle2 className='size-12 text-green-600' />} title='Payment successful'>
        <p className='text-gray-600'>
          We received {amountLabel}. {view === 'paid'
            ? 'Your order is confirmed and will be shipped soon.'
            : 'Your order is being confirmed; this takes a few seconds.'}
        </p>
        <p className='text-sm text-gray-500'>A receipt is on its way to your email.</p>
        <div className='flex flex-wrap justify-center gap-3'>
          <Link href='/orders' className={cn(buttonVariants())}>View my orders</Link>
          <Link href='/' className={cn(buttonVariants({ variant: 'outline' }))}>Continue shopping</Link>
        </div>
      </Panel>
    )
  }
  if (view === 'failed') {
    return (
      <Panel icon={<XCircle className='size-12 text-red-600' />} title='The payment did not go through'>
        <p className='text-gray-600'>{progress?.errorMessage ?? 'The order was cancelled or the payment failed.'}</p>
        <Link href='/orders' className={cn(buttonVariants({ variant: 'outline' }))}>Back to my orders</Link>
      </Panel>
    )
  }
  if (view === 'slow') {
    return (
      <Panel icon={<Clock className='size-10 text-gray-500' />} title='Still confirming your payment'>
        <p className='text-gray-600'>Stripe has not confirmed it yet. Check My orders in a minute; you will not be charged twice.</p>
        <Link href='/orders' className={cn(buttonVariants({ variant: 'outline' }))}>Go to my orders</Link>
      </Panel>
    )
  }
  return (
    <Panel icon={<Loader2 className='size-10 animate-spin text-gray-500' />} title='Confirming your payment…'>
      <p className='text-gray-600'>Waiting for Stripe. This usually takes a few seconds.</p>
    </Panel>
  )
}

function Panel({ icon, title, children }: { icon: ReactNode; title: string; children: ReactNode }) {
  return (
    <div className='flex flex-col items-center gap-4 rounded-2xl border bg-card p-10 text-center shadow-sm'>
      {icon}
      <h1 className='text-2xl font-semibold tracking-tight'>{title}</h1>
      {children}
    </div>
  )
}
