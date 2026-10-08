'use client'
import { Button } from '@/components/ui/button'
import { Dialog, DialogClose, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle, DialogTrigger } from '@/components/ui/dialog'
import { Field, FieldGroup } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import Link from 'next/link'
import React from 'react'

async function handleSubmit(e:React.SubmitEvent<HTMLFormElement>) {
  e.preventDefault()
}

function OrderForm({data}:{data:any}) {


  return (
    <div className='flex flex-col gap-8 sm:py-24 sm:px-12 p-4'>
      <div className='flex justify-between'>
          <h1 className='text-lg font-semibold'>Total: </h1>
          <p>{data.totalPrice}</p>
      </div>
      
        <Dialog>
          <DialogTrigger className='bg-black text-white text-lg px-6 py-3 hover:opacity-75 hover:cursor-pointer text-center w-full'>
            Go to checkout
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
              <Button type="submit" className="cursor-pointer">Save changes</Button>
            </DialogFooter>
          </form>
          </DialogContent>       
      </Dialog>
    </div>
  )
}

export default OrderForm