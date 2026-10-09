'use client'
import { Button } from '@/components/ui/button'
import { Dialog, DialogClose, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog'
import { Field, FieldGroup } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ArrowRight } from 'lucide-react'
import React from 'react'

async function handleSubmit(e:React.SubmitEvent<HTMLFormElement>) {
  e.preventDefault()
}

const formatPrice = (value: number | string) =>
  new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(Number(value))

function OrderForm({data}:{data:any}) {


  return (
    <div className='p-4 sm:px-12 sm:py-6'>
      <div className='flex flex-col gap-6 rounded-2xl border bg-card p-6 shadow-sm ml-16'>
        <h2 className='text-xl font-semibold tracking-tight'>Order summary</h2>

        <div className='flex items-baseline justify-between border-t pt-6'>
          <span className='text-base text-muted-foreground'>Total</span>
          <span className='text-2xl font-bold tabular-nums'>{formatPrice(data.totalPrice)}</span>
        </div>

        <Dialog>
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
                <Input id='country' name='country'  />
              </Field>
            </FieldGroup>
            <DialogFooter>
              <DialogClose render={<Button variant="outline" className="cursor-pointer">Cancel</Button>}></DialogClose>
              <Button type="submit" className="cursor-pointer">Place order</Button>
            </DialogFooter>
          </form>
          </DialogContent>       
      </Dialog>
      </div>
    </div>
  )
}

export default OrderForm