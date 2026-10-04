'use client'
import { toast } from '@/components/ui/toast'
import { Trash } from 'lucide-react'
import { useRouter } from 'next/navigation'
import React from 'react'

async function handleDelete(userId:string, productId:string, cookie:string | undefined, refresh: () => void) {    
    const body = {
        quantity: 0
    }
    const res = await fetch(`http://localhost:7000/api/v1/basket/${userId}/items/${productId}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${cookie}`},
        body: JSON.stringify(body)
    })

    refresh()
    toast.add({
        title: "Cart item deleted"
    })
}

function DeleteButton({productId, userId, cookie}: {productId:string, userId:string, cookie:string | undefined}) {
    const router = useRouter()
  return (
    <button className='flex gap-0.5 hover:cursor-pointer' onClick={() => handleDelete(userId,productId,cookie, () => router.refresh())}>
        <Trash color="#000000" />
        <p className='font-semibold hover:underline'>Delete</p>
    </button>
  )
}

export default DeleteButton