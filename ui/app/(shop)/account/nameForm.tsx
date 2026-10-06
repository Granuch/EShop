'use client'
import { toast } from '@/components/ui/toast'
import { useRouter } from 'next/navigation'
import React from 'react'
import { changePassword, updateProfile } from './actions'

type prop = {
    firstName:string,
    lastName:string,
}

type props = {
    name:prop,
}


function NameForm({name}:props) {

    const router = useRouter()

    async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
        e.preventDefault()
        
        const formData = new FormData(e.currentTarget)
        const firstName = formData.get("name")
        const lastName = formData.get("last")
        const currentpass = formData.get("oldPass")
        const newpass = formData.get("newPass")

        if(firstName || lastName) {
            const result = await updateProfile(
                String(firstName || name.firstName),
                String(lastName || name.lastName)
            )

            if(!result.success) {
                toast.add({
                    title: result.message,
                    type: 'error'
                })
                return
            }

        }

        if(currentpass && newpass) {
            const result = await changePassword(String(currentpass), String(newpass))

            if(!result.success) {
                toast.add({
                    title: result.message,
                    type: 'error'
                })
                return
            }
        }

        router.push("/")
        router.refresh()

    }

  return (
    <div className='flex flex-col gap-4 m-6 '>
        <h2 className='text-lg'>my data</h2>
        <form className="flex flex-col justify-center gap-8" onSubmit={handleSubmit}>
            <div className='flex gap-4'>
                <div className='flex flex-col'>
                    <label htmlFor="name" className='text-sm'>First name</label>
                    <input type="text" id='name' defaultValue={name.firstName} name='name' className='w-62 py-2 outline-none border-b focus:border-black transition-all'/>
                </div>
                <div className='flex flex-col'>
                    <label htmlFor="last" className='text-sm'>Last name</label>
                    <input type="text" id='last' name='last' defaultValue={name.lastName} className='w-62 py-2 outline-none border-b focus:border-black transition-all'/>
                </div>
            </div>
            <div className='flex flex-col gap-3'>
                <h2 className='text-lg'>Change password</h2>
                <div>
                    <label htmlFor="newPass" className='text-sm'>New password</label>
                    <input type="password" id='newPass' name='newPass' className='w-full py-2 outline-none border-b focus:border-black transition-all'/>
                </div>
                <div>
                    <label htmlFor="oldPass" className='text-sm'>Current password</label>
                    <input type="password" id='oldPass' name='oldPass' className='w-full py-2 outline-none border-b focus:border-black transition-all'/>
                </div>
            </div>
            <button className='bg-black text-white text-lg px-14 py-3 hover:opacity-75 hover:cursor-pointer ' type='submit'>Save Changes</button>
        </form>
    </div>
  )
}

export default NameForm