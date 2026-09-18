import { itemDatabyId } from '@/components/Item/types/itemType'
import Image from 'next/image'
import { notFound } from 'next/navigation'
import React from 'react'
import OrderForm from './orderForm'

type ProductpageProps = {
    params: Promise<{productId:string}>
}

async function fetchProductData(productId:string) {
    const res = await fetch(`http://localhost:7000/api/v1/products/${productId}`)
    if(!res.ok) notFound();

    return await res.json()
}

async function page({params}: ProductpageProps) {
    const {productId} = await params
    const product: itemDatabyId = await fetchProductData(productId)

  return (
    <div className='flex flex-col md:flex-row 2k:mx-62'>
      <div className={`${product.images.length <= 1 ? "flex justify-center items-center " : "grid grid-cols-1 md:grid-cols-2"} p-4 w-2/3 gap-1 `}>
          {product.images.map((image, index) => (<Image key={index} src={image.url} width={600} height={300} alt="test" className={`h-auto w-full   max-w-154 max-h-204 ${index > 0 ? "hidden md:block" : ""} `}/>))}
      </div>
      <div className='flex flex-col w-1/3 mx-18 p-10'>
          <p>{product.id}</p>
          <OrderForm productId={productId}/>
      </div>
    </div>
  )
}

export default page