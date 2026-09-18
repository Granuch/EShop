'use client'
import React from 'react'
import { addToCart } from './addToCart'

function OrderForm({productId}: {productId:string}) {  

    async function handleSubmit(e:React.SubmitEvent) {
      e.preventDefault()

      try {
        await addToCart(productId)
        console.log("Success")
      }
      catch(err) {
        console.error(err)
      }
    }

  return (
    <div>
        <form onSubmit={handleSubmit}>
            <button type='submit' className='text-white bg-black px-8 py-3 w-62 hover:cursor-pointer hover:opacity-75'>Add to cart</button>
        </form>
    </div>
  )
}

export default OrderForm