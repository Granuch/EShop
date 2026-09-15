import React from 'react'
import LoginForm from './loginForm'
import RegisterForm from './registerForm'
import { getSession } from '@/lib/session'

async function page() {
  const session = await getSession()

  return (
    <div className='flex'>
        <div className='flex justify-center w-screen h-screen'>
            <LoginForm/>
        </div>
        <div className='bg-gray-100 flex justify-center w-screen h-screen '>
            <RegisterForm/>
        </div>
    </div>
  )
}

export default page