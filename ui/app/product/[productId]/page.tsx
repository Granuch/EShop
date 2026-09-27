import { itemDatabyId } from '@/components/Item/types/itemType'
import Image from 'next/image'
import { notFound } from 'next/navigation'
import React from 'react'
import OrderForm from './orderForm'

type ProductpageProps = {
    params: Promise<{ productId: string }>
}

async function fetchProductData(productId: string) {
    const res = await fetch(`http://localhost:7000/api/v1/products/${productId}`)
    if (!res.ok) notFound();

    return await res.json()
}

async function page({ params }: ProductpageProps) {
    const { productId } = await params
    const product: itemDatabyId = await fetchProductData(productId)
    const images = product.images
    const count = images.length

    return (
        <div className='flex flex-col md:flex-row 2k:mx-62'>
            <div className='w-full md:w-2/3 p-4'>
                {count === 1 && (
                    <div className='relative w-full aspect-4/3 overflow-hidden rounded-2xl'>
                        <Image
                            src={images[0].url}
                            fill
                            sizes="(min-width: 768px) 66vw, 100vw"
                            alt="Product image"
                            className='object-cover'
                            priority
                        />
                    </div>
                )}

                {count === 2 && (
                    <div className='grid grid-cols-2 gap-2'>
                        {images.map((image, index) => (
                            <div key={index} className='relative aspect-square overflow-hidden rounded-2xl'>
                                <Image
                                    src={image.url}
                                    fill
                                    sizes="33vw"
                                    alt={`Product image ${index + 1}`}
                                    className='object-cover transition-transform duration-300 hover:scale-105'
                                    priority={index === 0}
                                />
                            </div>
                        ))}
                    </div>
                )}

                {count === 3 && (
                    <div className='grid grid-cols-2 grid-rows-2 gap-2 h-[420px] md:h-[500px]'>
                        <div className='relative row-span-2 overflow-hidden rounded-2xl'>
                            <Image
                                src={images[0].url}
                                fill
                                sizes="33vw"
                                alt="Product image 1"
                                className='object-cover transition-transform duration-300 hover:scale-105'
                                priority
                            />
                        </div>
                        {images.slice(1).map((image, index) => (
                            <div key={index} className='relative overflow-hidden rounded-2xl'>
                                <Image
                                    src={image.url}
                                    fill
                                    sizes="33vw"
                                    alt={`Product image ${index + 2}`}
                                    className='object-cover transition-transform duration-300 hover:scale-105'
                                />
                            </div>
                        ))}
                    </div>
                )}

                {count >= 4 && (
                    <div className='grid grid-cols-2 grid-rows-2 gap-2 h-[420px] md:h-[500px]'>
                        <div className='relative row-span-2 overflow-hidden rounded-2xl'>
                            <Image
                                src={images[0].url}
                                fill
                                sizes="33vw"
                                alt="Product image 1"
                                className='object-cover transition-transform duration-300 hover:scale-105'
                                priority
                            />
                        </div>
                        <div className='relative overflow-hidden rounded-2xl'>
                            <Image
                                src={images[1].url}
                                fill
                                sizes="33vw"
                                alt="Product image 2"
                                className='object-cover transition-transform duration-300 hover:scale-105'
                            />
                        </div>
                        <div className='relative overflow-hidden rounded-2xl'>
                            <Image
                                src={images[2].url}
                                fill
                                sizes="33vw"
                                alt="Product image 3"
                                className='object-cover transition-transform duration-300 hover:scale-105'
                            />
                            {count > 4 && (
                                <div className='absolute inset-0 bg-black/50 flex items-center justify-center text-white text-lg font-medium'>
                                    +{count - 3}
                                </div>
                            )}
                        </div>
                    </div>
                )}
            </div>

            <div className='flex flex-col w-full md:w-1/3 md:mx-18 p-10'>
                <p>{product.id}</p>
                <OrderForm productId={productId} />
            </div>
        </div>
    )
}

export default page