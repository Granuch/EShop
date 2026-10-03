import React from 'react'
import NameForm from './nameForm'
import { getSession } from '@/lib/session'
import { redirect } from 'next/navigation'

async function page() {

  const session = await getSession()

  if(!session) redirect("/")

  return (
    <div className='flex flex-col justify-center items-center m-6 gap-6'>
        <h2 className='text-xl text-black font-bold'>My account</h2>
        <NameForm name={session}/>
    </div>
  )
}

export default page