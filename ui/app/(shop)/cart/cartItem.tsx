import React from 'react'
import { basketItem, basketRes } from './types'
import Image from 'next/image'
import { Trash } from 'lucide-react'
import DeleteButton from './deleteButton'
import Link from 'next/link'

type itemProp = {
    prop: basketItem
}


async function CartItem({prop}:itemProp) {
  return (
    <div className='flex gap-4 w-full border-b border-border py-6 last:border-none sm:mx-6'>
        <Link href={`/product/${prop.productId}`} className='relative aspect-3/4 overflow-hidden bg-muted w-26'>
            <Image
            src={prop.mainImage || "/image-not-found-failure-network-260nw-2330163829.webp"}
            alt={prop.productName}
            fill
            sizes="(min-width: 1280px) 240px, (min-width: 768px) 33vw, 50vw"
            className="object-cover motion-safe:transition-transform motion-safe:duration-300 motion-safe:group-hover:scale-105"
            />
        </Link>
        <div className='flex flex-col justify-between w-2/3'>
            <Link href={`/product/${prop.productId}`} className='text-gray-500'>{prop.productName}</Link>
            <p>{prop.quantity}</p>
        </div>
        <div className='flex flex-col justify-between items-center'>
            <DeleteButton productId={prop.productId}/>
            <div>
                <p>{prop.subTotal}$</p>
            </div>
        </div>
    </div>
  )
}

export default CartItem