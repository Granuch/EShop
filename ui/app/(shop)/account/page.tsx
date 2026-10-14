import React from 'react'
import NameForm from './nameForm'
import { getSession } from '@/lib/session'
import { redirect } from 'next/navigation'
import { ResendConfirmationForm } from '@/components/Auth/emailForms'

async function page() {

  const session = await getSession()

  if(!session) redirect("/");

  return (
    <div className='flex flex-col justify-center items-center m-6 gap-6'>
        <h2 className='text-xl text-black font-bold'>My account</h2>
        <NameForm name={session}/>
        {!session.emailConfirmed && (
          <div className='flex w-full max-w-md flex-col gap-4'>
            <p className='text-red-400 text-lg'>
              Your email is not confirmed. Open the link we sent to {session.email} to be able to place orders.
            </p>
            <ResendConfirmationForm email={session.email}/>
          </div>
        )}
    </div>
  )
}

export default page
