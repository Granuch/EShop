'use client'
import Image from 'next/image'
import React from 'react'

function LoginForm() {

    function handleSubmit(e: React.SubmitEvent<HTMLElement>) {
        e.preventDefault()
        console.log("It works")
    }

  return (
    <form action="" onSubmit={(e) => handleSubmit(e)}>
        <div className='flex flex-col mt-36 ml-64 items-center gap-8'>
            <h2 className='text-2xl'>Already registered?</h2>
            <input type="text"  placeholder='Email' className='outline-none border-b-2 px-4 py-2 focus:border-black hover:border-gray-300 transition-all w-92'/>
            <input type="text" placeholder='Pasword' className='outline-none border-b-2 px-4 py-2 focus:border-black hover:border-gray-300 transition-all w-92'/>
            <button className='text-white text-lg bg-black w-full py-3 hover:cursor-pointer hover:opacity-75' type='submit'>Sign in</button>
            <div>
                <p className='text-sm text-gray-400'>Or continue by using</p>
                <div className='flex justify-center gap-2 mt-2'>
                    <Image src="\Без назви.svg" alt=""  width={30} height={30}/>
                    <Image src="\Без назвиgog.svg" alt="" width={30} height={30}/>
                </div>      
            </div>
        </div>
    </form>
  )
}

export default LoginForm