import { itemDatabyId } from '@/components/Item/types/itemType'
import Image from 'next/image'
import Link from 'next/link'
import { notFound } from 'next/navigation'
import React from 'react'
import AddToCartButton from './addToCartButton'
import { gatewayFetch } from '@/lib/api'
import { buildHref, findCategoryPath, getCategoryTree } from '@/lib/temp'
import { formatMoney } from '@/lib/admin/format'
import { Badge } from '@/components/ui/badge'
import { Breadcrumb, BreadcrumbItem, BreadcrumbLink, BreadcrumbList, BreadcrumbPage, BreadcrumbSeparator } from '@/components/ui/breadcrumb'
import { Table, TableBody, TableCell, TableRow } from '@/components/ui/table'

// Catalog's own low-stock default (GetLowStockProductsQuery.EffectiveThreshold), so "Only N left" matches the admin view.
const LOW_STOCK_THRESHOLD = 10

type ProductpageProps = {
    params: Promise<{ productId: string }>
}

async function fetchProductData(productId: string): Promise<itemDatabyId> {
    const res = await gatewayFetch(`/api/v1/products/${productId}`)
    if (!res.ok) notFound();
    return await res.json()
}

function PriceBlock({ price, discountPrice }: { price: number, discountPrice: number | null }) {
    if (discountPrice === null || discountPrice >= price) {
        return <p className='text-2xl font-semibold tabular-nums'>{formatMoney(price)}</p>
    }

    const percentOff = Math.round((1 - discountPrice / price) * 100)
    return (
        <div className='flex flex-wrap items-center gap-x-3 gap-y-1'>
            <p className='text-2xl font-semibold tabular-nums'>{formatMoney(discountPrice)}</p>
            <s className='text-muted-foreground tabular-nums'>
                <span className='sr-only'>Was </span>{formatMoney(price)}
            </s>
            <Badge>{`-${percentOff}%`}</Badge>
        </div>
    )
}

function StockStatus({ stock }: { stock: number }) {
    if (stock <= 0) return <Badge variant='destructive'>Out of stock</Badge>
    if (stock <= LOW_STOCK_THRESHOLD) return <Badge variant='outline'>{`Only ${stock} left`}</Badge>
    return <Badge variant='secondary'>In stock</Badge>
}

async function page({ params }: ProductpageProps) {
    const { productId } = await params
    const product = await fetchProductData(productId)
    const categoryPath = findCategoryPath(await getCategoryTree(), product.categoryId)
    const images = product.images
    const count = images.length
    const altFor = (index: number) => images[index]?.altText || `${product.name} image ${index + 1}`

    return (
        // w-full: the body is a flex column, where mx-auto alone would shrink the page to its content
        <div className='mx-auto w-full max-w-7xl px-4 pb-20 sm:px-6 lg:px-8'>
            <Breadcrumb className='mt-6'>
                <BreadcrumbList>
                    <BreadcrumbItem>
                        <BreadcrumbLink render={<Link href='/' />}>Home</BreadcrumbLink>
                    </BreadcrumbItem>
                    {categoryPath.map((category) => (
                        <React.Fragment key={category.id}>
                            <BreadcrumbSeparator />
                            <BreadcrumbItem>
                                <BreadcrumbLink render={<Link href={buildHref({ category: category.slug })} />}>
                                    {category.name}
                                </BreadcrumbLink>
                            </BreadcrumbItem>
                        </React.Fragment>
                    ))}
                    <BreadcrumbSeparator />
                    <BreadcrumbItem>
                        <BreadcrumbPage>{product.name}</BreadcrumbPage>
                    </BreadcrumbItem>
                </BreadcrumbList>
            </Breadcrumb>

            <div className='mt-6 flex flex-col gap-10 md:flex-row'>
                <div className='w-full md:w-3/5'>

                    {count === 0 && (
                        <div className='relative w-full aspect-4/3 overflow-hidden rounded-2xl bg-muted'>
                            <Image
                                src='/big_notFound.jpg'
                                fill
                                sizes="(min-width: 768px) 60vw, 100vw"
                                alt="No image available"
                                className='object-cover'
                                priority
                            />
                        </div>
                    )}

                    {count === 1 && (
                        <div className='relative w-full aspect-4/3 overflow-hidden rounded-2xl bg-muted'>
                            <Image
                                src={images[0].url}
                                fill
                                sizes="(min-width: 768px) 60vw, 100vw"
                                alt={altFor(0)}
                                className='object-cover'
                                priority
                            />
                        </div>
                    )}

                    {count === 2 && (
                        <div className='grid grid-cols-2 gap-2'>
                            {images.map((image, index) => (
                                <div key={image.id} className='relative aspect-square overflow-hidden rounded-2xl bg-muted'>
                                    <Image
                                        src={image.url}
                                        fill
                                        sizes="30vw"
                                        alt={altFor(index)}
                                        className='object-cover transition-transform duration-300 hover:scale-105'
                                        priority={index === 0}
                                    />
                                </div>
                            ))}
                        </div>
                    )}

                    {count === 3 && (
                        <div className='grid grid-cols-2 grid-rows-2 gap-2 h-[420px] md:h-[500px]'>
                            <div className='relative row-span-2 overflow-hidden rounded-2xl bg-muted'>
                                <Image
                                    src={images[0].url}
                                    fill
                                    sizes="30vw"
                                    alt={altFor(0)}
                                    className='object-cover transition-transform duration-300 hover:scale-105'
                                    priority
                                />
                            </div>
                            {images.slice(1).map((image, index) => (
                                <div key={image.id} className='relative overflow-hidden rounded-2xl bg-muted'>
                                    <Image
                                        src={image.url}
                                        fill
                                        sizes="30vw"
                                        alt={altFor(index + 1)}
                                        className='object-cover transition-transform duration-300 hover:scale-105'
                                    />
                                </div>
                            ))}
                        </div>
                    )}

                    {count >= 4 && (
                        <div className='grid grid-cols-2 grid-rows-2 gap-2 h-[420px] md:h-[500px]'>
                            <div className='relative row-span-2 overflow-hidden rounded-2xl bg-muted'>
                                <Image
                                    src={images[0].url}
                                    fill
                                    sizes="30vw"
                                    alt={altFor(0)}
                                    className='object-cover transition-transform duration-300 hover:scale-105'
                                    priority
                                />
                            </div>
                            <div className='relative overflow-hidden rounded-2xl bg-muted'>
                                <Image
                                    src={images[1].url}
                                    fill
                                    sizes="30vw"
                                    alt={altFor(1)}
                                    className='object-cover transition-transform duration-300 hover:scale-105'
                                />
                            </div>
                            <div className='relative overflow-hidden rounded-2xl bg-muted'>
                                <Image
                                    src={images[2].url}
                                    fill
                                    sizes="30vw"
                                    alt={altFor(2)}
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

                {/* min-w-0: the specifications table must scroll inside its container, not widen the page */}
                <div className='flex min-w-0 flex-col gap-6 md:w-2/5'>
                    <div className='space-y-2'>
                        <h1 className='text-3xl font-semibold tracking-tight'>{product.name}</h1>
                        <p className='text-sm text-muted-foreground'>SKU: <span className='font-mono'>{product.sku}</span></p>
                    </div>

                    <div className='space-y-3'>
                        <PriceBlock price={product.price} discountPrice={product.discountPrice} />
                        <StockStatus stock={product.stockQuantity} />
                    </div>

                    <AddToCartButton
                        productId={product.id}
                        productName={product.name}
                        disabled={product.stockQuantity <= 0}
                    />

                    {product.description && (
                        <p className='text-muted-foreground'>{product.description}</p>
                    )}

                    {product.attributes.length > 0 && (
                        <section aria-labelledby='specifications' className='space-y-3 border-t pt-6'>
                            <h2 id='specifications' className='text-lg font-semibold'>Specifications</h2>
                            <Table>
                                <TableBody>
                                    {product.attributes.map((attribute) => (
                                        <TableRow key={attribute.id}>
                                            <TableCell className='w-2/5 text-muted-foreground'>{attribute.name}</TableCell>
                                            <TableCell className='whitespace-normal'>{attribute.value}</TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </section>
                    )}
                </div>
            </div>
        </div>
    )
}

export default page
