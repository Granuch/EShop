'use client'
import { toast } from '@/components/ui/toast'
import { getSession } from '@/lib/session'
import { useRouter } from 'next/navigation'
import React from 'react'

type prop = {
    firstName:string,
    lastName:string,
}

type props = {
    name:prop,
    token?: string
}


function NameForm({name,token}:props) {

    const router = useRouter()

    async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
        e.preventDefault()
        
        const formData = new FormData(e.currentTarget)
        const firstName = formData.get("name")
        const lastName = formData.get("last")
        const currentpass = formData.get("oldPass")
        const newpass = formData.get("newPass")

        if(firstName || lastName) {
            const accountReq = {
                firstName: firstName || name.firstName,
                lastName: lastName || name.lastName
            }

            const res = await fetch("http://localhost:7000/api/v1/Account/profile", {
                method: "PUT",
                headers: { "Content-Type": "application/json", "Authorization": `Bearer ${token}` },
                body: JSON.stringify(accountReq)
            })

            const data = await res.json()

            if(!res.ok) {
                toast.add({
                    title: `${data.detail}`,
                    type: 'error'
                })
                return
            }

        }

        if(currentpass && newpass) {
            const newPassReq = {
                currentPassword: currentpass,
                newPassword: newpass
            }

            const res = await fetch("http://localhost:7000/api/v1/Account/change-password", {
                method: "POST",
                headers: { "Content-Type": "application/json", "Authorization": `Bearer ${token}` },
                body: JSON.stringify(newPassReq)
            })

            const data = await res.json()

            if(!res.ok) {
                toast.add({
                    title: `${data.detail}`,
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
                    <input type="text" id='newPass' name='newPass' className='w-full py-2 outline-none border-b focus:border-black transition-all'/>
                </div>
                <div>
                    <label htmlFor="oldPass" className='text-sm'>Current password</label>
                    <input type="text" id='oldPass' name='oldPass' className='w-full py-2 outline-none border-b focus:border-black transition-all'/>
                </div>
            </div>
            <button className='bg-black text-white text-lg px-14 py-3 hover:opacity-75 hover:cursor-pointer ' type='submit'>Save Changes</button>
        </form>
    </div>
  )
}

export default NameForm