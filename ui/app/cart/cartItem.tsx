import React from 'react'
import { basketItem, basketRes } from './types'
import Image from 'next/image'
import { Trash } from 'lucide-react'

type itemProp = {
    prop: basketItem
}

function CartItem({prop}:itemProp) {
  return (
    <div className='flex gap-4 w-full border-b border-border py-6 last:border-none sm:mx-6'>
        <div className='relative aspect-3/4 overflow-hidden bg-muted w-26'>
            <Image
            src={"/372KT-MLC-030-2-1325574.avif"}
            alt={prop.productName}
            fill
            sizes="(min-width: 1280px) 240px, (min-width: 768px) 33vw, 50vw"
            className="object-cover motion-safe:transition-transform motion-safe:duration-300 motion-safe:group-hover:scale-105"
            />
        </div>
        <div className='flex flex-col justify-between w-2/3'>
            <h2 className='text-gray-500'>{prop.productName}</h2>
            <p>{prop.quantity}</p>
        </div>
        <div className='flex flex-col justify-between items-center'>
            <button className='flex gap-0.5 hover:cursor-pointer'>
                <Trash color="#000000" />
                <p className='font-semibold'>Delete</p>
            </button>
            <div>
                <p>{prop.subTotal}$</p>
            </div>
        </div>
    </div>
  )
}

export default CartItem