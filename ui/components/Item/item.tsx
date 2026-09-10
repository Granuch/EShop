import React from 'react'
import Image from "next/image";
import Link from 'next/link';

function Item() {

    const item: {id:number; name:string; desc:string} = {
        id: 1,
        name: "Піджак у стилі Наполеон",
        desc: "2 599 UAH"
    }

  return (
    <div className='w-fit'>
        <Link href="/test" className='hover:cursor-pointer'>
            <Image 
                src="/372KT-MLC-030-2-1325574.avif"
                alt={item.name}
                width={300}
                height={300}
            />
            <div className='flex flex-col gap-0.5 mt-0.5'>
                <p className=''>{item.name}</p>
                <p className='text-sm'>{item.desc}</p>
            </div>
        </Link>
    </div>
  ) 
}

export default Item