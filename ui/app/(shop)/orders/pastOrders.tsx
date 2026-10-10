'use client'
import React, { useCallback, useEffect, useState } from 'react'
import Link from 'next/link'
import { ChevronDown, PackageOpen } from 'lucide-react'
import { fetchUserOrders } from './fetchOrders'

type OrderItem = {
  id: string
  productId: string
  productName: string
  unitPrice: number
  quantity: number
  subTotal: number
}

type ShippingAddress = {
  street: string
  city: string
  state: string
  zipCode: string
  country: string
}

type Order = {
  id: string
  totalPrice: number
  status: string
  createdAt: string
  paidAt: string | null
  shippedAt: string | null
  deliveredAt: string | null
  cancelledAt: string | null
  cancellationReason: string | null
  shippingAddress: ShippingAddress
  items: OrderItem[]
}

type PagedOrders = {
  items: Order[]
  pageNumber: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
}

const PAGE_SIZE = 10

const statusStyles: Record<string, string> = {
  pending: 'bg-yellow-100 text-yellow-800',
  paid: 'bg-blue-100 text-blue-800',
  shipped: 'bg-indigo-100 text-indigo-800',
  delivered: 'bg-green-100 text-green-800',
  cancelled: 'bg-red-100 text-red-800',
}

const money = (n: number) =>
  new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(n)

const date = (iso: string) =>
  new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' })

// Latest milestone that has happened, shown as a one-line summary in the expanded view
function latestEvent(o: Order): string | null {
  if (o.cancelledAt) return `Cancelled on ${date(o.cancelledAt)}`
  if (o.deliveredAt) return `Delivered on ${date(o.deliveredAt)}`
  if (o.shippedAt) return `Shipped on ${date(o.shippedAt)}`
  if (o.paidAt) return `Paid on ${date(o.paidAt)}`
  return null
}

function OrderCard({ order }: { order: Order }) {
  const [open, setOpen] = useState(false)
  const badge = statusStyles[order.status.toLowerCase()] ?? 'bg-gray-100 text-gray-800'
  const event = latestEvent(order)
  const itemCount = order.items.reduce((sum, i) => sum + i.quantity, 0)
  const a = order.shippingAddress

  return (
    <li className='border border-gray-200'>
      <button
        type='button'
        onClick={() => setOpen(v => !v)}
        aria-expanded={open}
        className='flex w-full items-center justify-between gap-4 px-5 py-4 text-left hover:bg-gray-50 transition-colors'
      >
        <div className='flex flex-col gap-1'>
          <span className='font-medium'>Order #{order.id.slice(0, 8)}</span>
          <span className='text-sm text-gray-500'>
            {date(order.createdAt)} · {itemCount} {itemCount === 1 ? 'item' : 'items'}
          </span>
        </div>

        <div className='flex items-center gap-4'>
          <span className={`px-2.5 py-1 text-xs font-medium ${badge}`}>{order.status}</span>
          <span className='font-medium tabular-nums'>{money(order.totalPrice)}</span>
          <ChevronDown
            size={18}
            className={`transition-transform ${open ? 'rotate-180' : ''}`}
          />
        </div>
      </button>

      {open && (
        <div className='border-t border-gray-200 px-5 py-4'>
          <ul className='divide-y divide-gray-100'>
            {order.items.map(item => (
              <li key={item.id} className='flex items-center gap-4 py-3'>
                <Link href={`/products/${item.productId}`} className='flex-1 hover:underline'>
                  {item.productName}
                </Link>
                <span className='text-sm text-gray-500 tabular-nums'>
                  {money(item.unitPrice)} × {item.quantity}
                </span>
                <span className='w-28 text-right tabular-nums'>{money(item.subTotal)}</span>
              </li>
            ))}
          </ul>

          <div className='mt-4 grid gap-4 border-t border-gray-100 pt-4 text-sm sm:grid-cols-2'>
            <div>
              <p className='font-medium'>Shipping address</p>
              <p className='text-gray-600'>
                {a.street}
                <br />
                {a.city}, {a.state} {a.zipCode}
                <br />
                {a.country}
              </p>
            </div>
            <div>
              <p className='font-medium'>Status</p>
              <p className='text-gray-600'>{event ?? 'Waiting for payment'}</p>
              {order.cancellationReason && (
                <p className='text-gray-600'>Reason: {order.cancellationReason}</p>
              )}
            </div>
          </div>
        </div>
      )}
    </li>
  )
}

function Skeleton() {
  return (
    <ul className='flex flex-col gap-3' aria-busy='true'>
      {[0, 1, 2].map(i => (
        <li key={i} className='h-[74px] animate-pulse border border-gray-200 bg-gray-50' />
      ))}
    </ul>
  )
}

export default function PastOrders() {
  const [page, setPage] = useState(1)
  const [data, setData] = useState<PagedOrders | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async (pageNumber: number) => {
    setError(null)
    setData(null)

    try {
      let res = await fetchUserOrders(pageNumber, PAGE_SIZE)

      if (!res.ok) throw new Error('Could not load your orders.')
      setData(res.data)
    } catch (err: any) {
      setError(err.message ?? 'Something went wrong.')
    }
  }, [])

  useEffect(() => {
    load(page)
  }, [page, load])

  if (error) {
    return (
      <div className='flex flex-col items-start gap-3'>
        <p className='text-red-600'>{error}</p>
        <button onClick={() => load(page)} className='border px-4 py-2 hover:bg-gray-50'>
          Try again
        </button>
      </div>
    )
  }

  if (!data) return <Skeleton />

  if (data.items.length === 0) {
    return (
      <div className='flex flex-col items-center gap-4 py-16 text-center'>
        <PackageOpen size={40} className='text-gray-400' />
        <p className='text-lg'>You haven't placed any orders yet.</p>
        <Link href='/' className='bg-black px-6 py-3 text-white hover:opacity-75'>
          Start shopping
        </Link>
      </div>
    )
  }

  return (
    <div className='flex flex-col gap-6'>
      <ul className='flex flex-col gap-3'>
        {data.items.map(order => (
          <OrderCard key={order.id} order={order} />
        ))}
      </ul>

      {data.totalPages > 1 && (
        <div className='flex items-center justify-between'>
          <button
            disabled={!data.hasPreviousPage}
            onClick={() => setPage(p => p - 1)}
            className='border px-4 py-2 hover:bg-gray-50 disabled:opacity-40 disabled:hover:bg-transparent'
          >
            Previous
          </button>
          <span className='text-sm text-gray-500'>
            Page {data.pageNumber} of {data.totalPages}
          </span>
          <button
            disabled={!data.hasNextPage}
            onClick={() => setPage(p => p + 1)}
            className='border px-4 py-2 hover:bg-gray-50 disabled:opacity-40 disabled:hover:bg-transparent'
          >
            Next
          </button>
        </div>
      )}
    </div>
  )
}