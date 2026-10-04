import React from 'react'
import NameForm from './nameForm'
import { getSession } from '@/lib/session'
import { redirect } from 'next/navigation'
import { cookies } from 'next/headers'

async function page() {

  const session = await getSession()

  if(!session) redirect("/");

  const token = (await cookies()).get("access_token")?.value

  return (
    <div className='flex flex-col justify-center items-center m-6 gap-6'>
        <h2 className='text-xl text-black font-bold'>My account</h2>
        <NameForm name={session} token={token}/>
    </div>
  )
}

export default page