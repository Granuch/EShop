'use client'
import { Loader2, XCircle } from 'lucide-react'
import { useRouter } from 'next/navigation'
import { useState } from 'react'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog'
import { Button } from '@/components/ui/button'
import { toast } from '@/components/ui/toast'
import { cancelOrder } from '@/lib/payments'

// Ordering requires a reason; offering a few keeps it one click for the customer.
const REASONS = [
  'I changed my mind',
  'I ordered by mistake',
  'I found a better price elsewhere',
  'I want to change the items or the address',
  'Other reason',
]

type Props = {
  orderId: string
  /** Called after a successful cancel; without it the page is refreshed. */
  onCancelled?: () => void
  /** Where to go after a successful cancel (the payment page sends the customer back to My orders). */
  redirectTo?: string
  size?: 'sm' | 'default'
}

export function CancelOrderButton({ orderId, onCancelled, redirectTo, size = 'sm' }: Props) {
  const router = useRouter()
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState(REASONS[0])
  const [comment, setComment] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function confirm() {
    setPending(true)
    setError(null)
    const full = comment.trim() ? `${reason}: ${comment.trim()}` : reason
    const result = await cancelOrder(orderId, full)
    setPending(false)
    if (!result.ok) {
      setError(result.message)
      return
    }
    setOpen(false)
    toast.add({ title: 'Order cancelled', description: 'Nothing was charged.' })
    if (redirectTo) router.push(redirectTo)
    else if (onCancelled) onCancelled()
    else router.refresh()
  }

  return (
    <AlertDialog open={open} onOpenChange={(next) => { setOpen(next); if (!next) setError(null) }}>
      <AlertDialogTrigger render={<Button type='button' variant='outline' size={size} />}>
        <XCircle /> Cancel order
      </AlertDialogTrigger>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Cancel this order?</AlertDialogTitle>
          <AlertDialogDescription>
            The order is not paid yet, so nothing is charged. This cannot be undone; you can always order again.
          </AlertDialogDescription>
        </AlertDialogHeader>

        <fieldset className='flex flex-col gap-2 text-sm' disabled={pending}>
          <legend className='mb-1 font-medium'>Why are you cancelling?</legend>
          {REASONS.map((r) => (
            <label key={r} className='flex cursor-pointer items-center gap-2'>
              <input type='radio' name={`cancel-reason-${orderId}`} value={r} checked={reason === r}
                onChange={() => setReason(r)} className='size-4 accent-black' />
              {r}
            </label>
          ))}
          <textarea
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            maxLength={300}
            rows={2}
            placeholder='Anything to add? (optional)'
            aria-label='Comment'
            className='mt-2 w-full resize-none rounded-md border px-3 py-2 outline-none focus-visible:ring-2 focus-visible:ring-ring'
          />
        </fieldset>

        {error && <p role='alert' className='rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700'>{error}</p>}

        <AlertDialogFooter>
          <AlertDialogCancel disabled={pending}>Keep the order</AlertDialogCancel>
          <AlertDialogAction variant='destructive' disabled={pending} onClick={confirm}>
            {pending ? <><Loader2 className='animate-spin' /> Cancelling…</> : 'Cancel order'}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
