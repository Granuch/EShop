'use client'
import { Trash } from 'lucide-react'
import { useRouter } from 'next/navigation'
import React from 'react'
import { removeFromCart } from './actions'

function DeleteButton({productId}: {productId:string}) {
    const router = useRouter()

    async function handleDelete() {
        await removeFromCart(productId)
        router.refresh()
    }

  return (
    <button className='flex gap-0.5 hover:cursor-pointer' onClick={handleDelete}>
        <Trash color="#000000" />
        <p className='font-semibold hover:underline'>Delete</p>
    </button>
  )
}

export default DeleteButton
