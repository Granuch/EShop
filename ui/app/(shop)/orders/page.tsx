import React from 'react'
import PastOrders from './pastOrders'

function page() {
  return (
     <main className='mx-auto max-w-3xl px-4 py-10 flex flex-col items-center'>
      <h1 className='mb-6 text-2xl'>My orders</h1>
      <PastOrders />
    </main>
  )
}

export default page