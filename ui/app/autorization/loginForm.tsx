'use client'
import Image from 'next/image'
import { useRouter } from 'next/navigation'
import React from 'react'

function LoginForm() {

    const router = useRouter()

    async function handleSubmit(e: React.SubmitEvent<HTMLFormElement>) {
        e.preventDefault()
        const formData = new FormData(e.currentTarget)
        const body = Object.fromEntries(formData.entries())

        try {
            const res = await fetch("/api/auth/login", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(body)
            })

            const data = await res.json()

            if(!res.ok) throw new Error(data.message || "Failed to log in");


            router.push("/")
            router.refresh()
        }
        catch(err: any) {
            console.error(err.message)
        }

    }

  return (
    <form onSubmit={handleSubmit}>
        <div className='flex flex-col mt-36 ml-64 items-center gap-8'>
            <h2 className='text-2xl'>Already registered?</h2>
            <input type="text"  placeholder='Email' name='Email' className='outline-none border-b-2 px-4 py-2 focus:border-black hover:border-gray-300 transition-all w-92'/>
            <input type="password" name='Password' placeholder='Pasword' className='outline-none border-b-2 px-4 py-2 focus:border-black hover:border-gray-300 transition-all w-92'/>
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