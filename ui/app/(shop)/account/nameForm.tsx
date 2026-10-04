'use client'
import React from 'react'

type prop = {
    firstName:string,
    lastName:string
}

function NameForm({name}:{name:prop}) {
  return (
    <div className='flex flex-col gap-4 m-6 '>
        <h2 className='text-lg'>my data</h2>
        <form className="flex flex-col justify-center gap-8">
            <div className='flex gap-4'>
                <div className='flex flex-col'>
                    <label htmlFor="name" className='text-sm'>First name</label>
                    <input type="text" defaultValue={name.firstName} name='name' className='w-62 py-2 outline-none border-b focus:border-black transition-all'/>
                </div>
                <div className='flex flex-col'>
                    <label htmlFor="last" className='text-sm'>Last name</label>
                    <input type="text" name='last' defaultValue={name.lastName} className='w-62 py-2 outline-none border-b focus:border-black transition-all'/>
                </div>
            </div>
            <div className='flex flex-col gap-3'>
                <h2 className='text-lg'>Change password</h2>
                <div>
                    <label htmlFor="newPass" className='text-sm'>New password</label>
                    <input type="text" name='newPass' className='w-full py-2 outline-none border-b focus:border-black transition-all'/>
                </div>
                <div>
                    <label htmlFor="oldPass" className='text-sm'>Current password</label>
                    <input type="text" name='oldPass' className='w-full py-2 outline-none border-b focus:border-black transition-all'/>
                </div>
            </div>
            <button className='bg-black text-white text-lg px-14 py-3 hover:opacity-75 hover:cursor-pointer '>Save Changes</button>
        </form>
    </div>
  )
}

export default NameForm