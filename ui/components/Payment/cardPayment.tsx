'use client'
import { Elements, PaymentElement, useElements, useStripe } from '@stripe/react-stripe-js'
import { loadStripe, type Appearance, type Stripe } from '@stripe/stripe-js'
import { CreditCard, Loader2, Lock } from 'lucide-react'
import Link from 'next/link'
import { useRouter } from 'next/navigation'
import React, { useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import { startPayment } from '@/lib/payments'

// One Stripe.js instance per page load; loadStripe injects js.stripe.com once.
let stripePromise: Promise<Stripe | null> | null = null
function getStripe(publishableKey: string) {
  // The developer assistant is a floating "stripe" badge Stripe.js shows with test keys; visitors should not see it.
  stripePromise ??= loadStripe(publishableKey, { developerTools: { assistant: { enabled: false } } })
  return stripePromise
}

// The card fields render in Stripe's iframe, so they take the shop's look from here, not from Tailwind.
const appearance: Appearance = {
  theme: 'stripe',
  variables: {
    colorPrimary: '#000000',
    colorText: '#111111',
    colorDanger: '#dc2626',
    fontFamily: 'Roboto, system-ui, sans-serif',
    borderRadius: '8px',
    spacingUnit: '4px',
  },
  rules: {
    '.Input': { boxShadow: 'none', border: '1px solid #e5e5e5' },
    '.Input:focus': { boxShadow: '0 0 0 2px rgba(0,0,0,0.15)', borderColor: '#000000' },
    '.Label': { fontWeight: '500' },
  },
}

const fonts = [{ cssSrc: 'https://fonts.googleapis.com/css2?family=Roboto:wght@400;500&display=swap' }]

// Payment records the order's payment from a message 1 to 15 s after the order exists, so create-intent answers
// "not ready" for a while: retry with back-off for about half a minute.
const RETRY_DELAYS_MS = [1000, 1000, 2000, 2000, 3000, 3000, 4000, 4000, 5000, 5000]

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms))

type Setup =
  | { state: 'loading' }
  | { state: 'ready'; clientSecret: string }
  | { state: 'closed' | 'error'; message: string }

export function CardPayment({ orderId, publishableKey, amountLabel }: { orderId: string; publishableKey: string; amountLabel: string }) {
  const router = useRouter()
  const [setup, setSetup] = useState<Setup>({ state: 'loading' })
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let cancelled = false
    async function run() {
      for (let i = 0; ; i++) {
        const result = await startPayment(orderId)
        if (cancelled) return
        if (result.kind === 'ready') {
          // Already paid (or being processed) in another tab: confirming again would be refused.
          if (result.intentStatus === 'succeeded' || result.intentStatus === 'processing') {
            router.replace(`/orders/${orderId}/pay/complete`)
            return
          }
          setSetup({ state: 'ready', clientSecret: result.clientSecret })
          return
        }
        if (result.kind === 'closed' || result.kind === 'error') {
          setSetup({ state: result.kind, message: result.message })
          return
        }
        if (i >= RETRY_DELAYS_MS.length) {
          setSetup({ state: 'error', message: 'The payment is taking longer than usual to get ready. Please try again in a minute.' })
          return
        }
        await sleep(RETRY_DELAYS_MS[i])
        if (cancelled) return
      }
    }
    run()
    return () => { cancelled = true }
  }, [orderId, router, attempt])

  if (setup.state === 'loading') {
    return (
      <div className='flex items-center gap-3 rounded-xl border p-6 text-sm text-muted-foreground'>
        <Loader2 className='size-4 animate-spin' /> Preparing a secure payment…
      </div>
    )
  }

  if (setup.state !== 'ready') {
    return (
      <div role='alert' className='flex flex-col items-start gap-4 rounded-xl border p-6'>
        <p className={setup.state === 'error' ? 'text-red-600' : 'text-gray-700'}>{setup.message}</p>
        {setup.state === 'error'
          ? <Button variant='outline' onClick={() => { setSetup({ state: 'loading' }); setAttempt((a) => a + 1) }}>Try again</Button>
          : <Link href='/orders' className='underline underline-offset-4'>Back to my orders</Link>}
      </div>
    )
  }

  return (
    <Elements
      stripe={getStripe(publishableKey)}
      options={{ clientSecret: setup.clientSecret, appearance, fonts, loader: 'always' }}
    >
      <CardForm orderId={orderId} amountLabel={amountLabel} />
    </Elements>
  )
}

function CardForm({ orderId, amountLabel }: { orderId: string; amountLabel: string }) {
  const stripe = useStripe()
  const elements = useElements()
  const router = useRouter()
  const [ready, setReady] = useState(false)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleSubmit(e: React.SubmitEvent<HTMLFormElement>) {
    e.preventDefault()
    if (!stripe || !elements) return

    setPending(true)
    setError(null)
    // Cards that need 3-D Secure redirect to return_url; every other card stays on this page.
    const { error: stripeError, paymentIntent } = await stripe.confirmPayment({
      elements,
      confirmParams: { return_url: `${window.location.origin}/orders/${orderId}/pay/complete` },
      redirect: 'if_required',
    })

    if (stripeError) {
      // A declined card keeps the same payment open, so the customer can simply try another card.
      setError(stripeError.message ?? 'The payment could not be completed. Please try again.')
      setPending(false)
      return
    }
    if (paymentIntent && (paymentIntent.status === 'succeeded' || paymentIntent.status === 'processing')) {
      router.push(`/orders/${orderId}/pay/complete`)
      return
    }
    setError('The payment was not completed. Please try again.')
    setPending(false)
  }

  return (
    <form onSubmit={handleSubmit} className='flex flex-col gap-5'>
      {/* Cards only: the intent allows nothing else, and Link would add its own sign-up form and bank/Klarna tabs. */}
      <PaymentElement
        options={{ layout: 'tabs', wallets: { link: 'never', applePay: 'never', googlePay: 'never' } }}
        onReady={() => setReady(true)}
      />
      {error && <p role='alert' className='rounded-lg bg-red-50 px-4 py-3 text-sm text-red-700'>{error}</p>}
      <Button type='submit' size='lg' className='h-12 w-full text-base' disabled={!stripe || !ready || pending}>
        {pending
          ? <><Loader2 className='animate-spin' /> Processing…</>
          : <><CreditCard /> Pay {amountLabel}</>}
      </Button>
      <p className='flex items-center justify-center gap-1.5 text-xs text-muted-foreground'>
        <Lock className='size-3' /> Card details go straight to Stripe and never touch this shop&apos;s servers.
      </p>
    </form>
  )
}
