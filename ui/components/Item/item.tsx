import React from 'react'
import Image from "next/image";
import Link from 'next/link';
import { itemData } from './types/itemType';

type itemProp = {
    itemData:itemData
}

function Item({itemData}:itemProp) {

  return (
    <div className='w-fit'>
        <Link href={`/product/${itemData.id}`} className='hover:cursor-pointer'>
        <div className='w-75 h-100 relative'>
            <Image 
                src={itemData.mainImageUrl ? itemData.mainImageUrl : "/372KT-MLC-030-2-1325574.avif"}
                alt={itemData.name}
                fill
            />
        </div>
            <div className='flex flex-col gap-0.5 mt-0.5'>
                <p className=''>{itemData.name}</p>
                <p className='text-sm'>{`${itemData.description ? itemData.description : ""}`}</p>
            </div>
        </Link>
    </div>
  ) 
}

export default Item