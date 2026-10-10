'use client'
import { Button } from '@/components/ui/button'
import { Dialog, DialogClose, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog'
import { Field, FieldGroup } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ArrowRight } from 'lucide-react'
import React, { useState } from 'react'
import { useRouter } from 'next/navigation'
import { checkoutData, checkoutObj, fetchCheckout } from './checkoutFetch'
import { toast } from '@/components/ui/toast'
import { Select, SelectContent, SelectGroup, SelectItem, SelectLabel, SelectTrigger, SelectValue } from '@/components/ui/select'



const countries = [
  { label: "Select a country", value: null },
  { label: "United States", value: "US" },
  { label: "United Kingdom", value: "GB" },
  { label: "Canada", value: "CA" },
  { label: "Germany", value: "DE" },
  { label: "France", value: "FR" },
  { label: "Italy", value: "IT" },
  { label: "Spain", value: "ES" },
  { label: "Poland", value: "PL" },
  { label: "Ukraine", value: "UA" },
  { label: "Netherlands", value: "NL" },
  { label: "Sweden", value: "SE" },
  { label: "Switzerland", value: "CH" },
  { label: "Australia", value: "AU" },
  { label: "Japan", value: "JP" },
  { label: "China", value: "CN" },
  { label: "India", value: "IN" },
  { label: "Brazil", value: "BR" },
  { label: "Mexico", value: "MX" },
  { label: "Turkey", value: "TR" },
  { label: "United Arab Emirates", value: "AE" },
]

const formatPrice = (value: number | string) =>
  new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(Number(value))

  function OrderForm({data}:{data:any}) {
    const [open, setOpen] = useState(false)
    const [pending, setPending] = useState(false)
    // Shown inside the dialog: a toast would sit under the dialog's backdrop and could not be read.
    const [error, setError] = useState<string | null>(null)
    const router = useRouter()

    async function handleSubmit(e:React.SubmitEvent<HTMLFormElement>) {
    e.preventDefault()
    const formData = new FormData(e.currentTarget)
    const reqData: checkoutData = Object.fromEntries(formData.entries()) as unknown as checkoutData
    const body: checkoutObj = {
      shippingAddress: reqData
    }

    setPending(true)
    setError(null)
    try {
      const result = await fetchCheckout(body)

      // On a failure the dialog stays open, so the address can be corrected without typing it again.
      if(!result.ok) {
        setError(result.error)
        return
      }

      // Ordering creates the order from a message, so it reaches My orders a few seconds after this.
      toast.add({
        title: "Order successfully placed",
        description: "It will appear in My orders in a moment."
      })
      setOpen(false)
      // Checkout empties the basket; show it.
      router.refresh()
    }
    catch {
      setError('The order could not be placed. Please try again')
    }
    finally {
      setPending(false)
    }
  }

  return (
    <div className='p-4 sm:px-12 sm:py-6'>
      <div className='flex flex-col gap-6 rounded-2xl border bg-card p-6 shadow-sm ml-16'>
        <h2 className='text-xl font-semibold tracking-tight'>Order summary</h2>

        <div className='flex items-baseline justify-between border-t pt-6'>
          <span className='text-base text-muted-foreground'>Total</span>
          <span className='text-2xl font-bold tabular-nums'>{formatPrice(data.totalPrice)}</span>
        </div>

        <Dialog open={open} onOpenChange={(next) => { setOpen(next); if (!next) setError(null) }}>
          <DialogTrigger className='group flex w-full cursor-pointer items-center justify-center gap-2 rounded-lg bg-black px-6 py-3.5 text-base font-medium text-white transition hover:bg-black/80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-black focus-visible:ring-offset-2 active:scale-[0.99]'>
            Go to checkout
            <ArrowRight className='size-4 transition-transform group-hover:translate-x-0.5' />
          </DialogTrigger>
          <DialogContent>
            <form onSubmit={handleSubmit}>
            <DialogHeader>
              <DialogTitle>Enter shipping data</DialogTitle>
              <DialogDescription>Please enter data for shipping</DialogDescription>
            </DialogHeader>
            <FieldGroup className='py-6'>
              <Field>
                <Label htmlFor='street'>Street</Label>
                <Input id='street' name='street'  />
              </Field>
              <Field>
                <Label htmlFor='city'>City</Label>
                <Input id='city' name='city'  />
              </Field>
              <Field>
                <Label htmlFor='state'>State</Label>
                <Input id='state' name='state'  />
              </Field>
              <Field>
                <Label htmlFor='zipCode'>Zip Code</Label>
                <Input id='zipCode' name='zipCode'  />
              </Field>
              <Field>
                <Label htmlFor='country'>Country</Label>
                <Select items={countries} name='country' required>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                  <SelectGroup>
                    <SelectLabel>Countries</SelectLabel>
                    {countries.map((country) => (
                      <SelectItem key={country.value} value={country.value}>
                        {country.label}
                      </SelectItem>
                    ))}
                  </SelectGroup>
                </SelectContent>
                </Select>
              </Field>
            </FieldGroup>
            {error && <p role='alert' className='pb-4 text-sm text-red-600'>{error}</p>}
            <DialogFooter>
              <DialogClose render={<Button variant="outline" className="cursor-pointer">Cancel</Button>}></DialogClose>
              <Button type="submit" className="cursor-pointer" disabled={pending}>{pending ? 'Placing order…' : 'Place order'}</Button>
            </DialogFooter>
          </form>
          </DialogContent>       
      </Dialog>
      </div>
    </div>
  )
}

export default OrderForm