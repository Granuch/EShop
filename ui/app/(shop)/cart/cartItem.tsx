import React from 'react'
import { basketItem } from './types'
import Image from 'next/image'
import DeleteButton from './deleteButton'
import Link from 'next/link'

type itemProp = {
    prop: basketItem
}


async function CartItem({prop}:itemProp) {
  return (
    <div className='flex w-full gap-4 border-b border-border py-6 last:border-none sm:gap-6'>
        <Link
          href={`/product/${prop.productId}`}
          className='group relative aspect-3/4 w-24 shrink-0 overflow-hidden rounded-xl bg-muted ring-1 ring-border sm:w-28'
        >
            <Image
            src={prop.mainImage || "/image-not-found-failure-network-260nw-2330163829.webp"}
            alt={prop.productName}
            fill
            sizes="(min-width: 1280px) 240px, (min-width: 768px) 33vw, 50vw"
            className="object-cover motion-safe:transition-transform motion-safe:duration-300 motion-safe:group-hover:scale-105"
            />
        </Link>

        <div className='flex min-w-0 flex-1 flex-col justify-between py-1'>
            <Link
              href={`/product/${prop.productId}`}
              className='line-clamp-2 text-base font-medium text-foreground transition-colors hover:text-muted-foreground hover:underline underline-offset-4'
            >
              {prop.productName}
            </Link>
            <p className='inline-flex w-fit items-center gap-1.5 rounded-full bg-muted px-3 py-1 text-sm text-muted-foreground'>
              Qty
              <span className='font-semibold tabular-nums text-foreground'>{prop.quantity}</span>
            </p>
        </div>

        <div className='flex shrink-0 flex-col items-end justify-between py-1'>
            <DeleteButton productId={prop.productId}/>
            <div>
                <p className='text-lg font-semibold tabular-nums'>{prop.subTotal}$</p>
            </div>
        </div>
    </div>
  )
}

export default CartItem