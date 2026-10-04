'use client'
import React from 'react'
import { addToCart } from './addToCart'
import { getSession } from '@/lib/session'
import { itemDatabyId } from '@/components/Item/types/itemType'
import { toast } from '@/components/ui/toast'

function OrderForm({product}: {product:itemDatabyId}) {  
    async function handleSubmit(e:React.SubmitEvent) {
      e.preventDefault()

      try {
        const result = await addToCart(product.id)
        if(!result.success) {
          toast.add({
            title: `Something went wrong`,
            type: 'error'
          })
          return
        }
        toast.add({
            title: `${product.name} was added to your cart`,
            type: 'success'
          })
      }
      catch(err) {
        console.error(err)
      }
    }

  return (
    <div>
        <form onSubmit={handleSubmit}>
          <div className='flex flex-col gap-2'>
            <h2 className='text-2xl'>{product.name}</h2>
            <p className=' text-gray-400'>{product.description}</p>
            <button type='submit' className='text-white bg-black px-8 py-3 w-62 hover:cursor-pointer hover:opacity-75 mt-4'>Add to cart</button>
          </div>
        </form>
    </div>
  )
}

export default OrderForm