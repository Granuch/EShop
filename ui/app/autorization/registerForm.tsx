import React from 'react'

function RegisterForm() {
  return (
    <form action="">
        <div className='flex flex-col mt-35 mr-64 items-start gap-8'>
            <h2 className='text-2xl w-full text-center'>Is this your first visit?</h2>
            <input type="text" placeholder='Name' className='outline-none border-b-2 px-4 py-2 focus:border-black hover:border-gray-300 transition-all w-92'/>
            <input type="email"  placeholder='Email' className='outline-none border-b-2 px-4 py-2 focus:border-black hover:border-gray-300 transition-all w-92'/>
            <input type="password" placeholder='Pasword' className='outline-none border-b-2 px-4 py-2 focus:border-black hover:border-gray-300 transition-all w-92'/>
            <div className='flex flex-col gap-3'>
              <label className='flex gap-2 text-[15px]'>
                <input type="checkbox" name="" id="" className='accent-black w-5 h-5'/>
                <p>Yes, I would like to sign up for the newsletter</p>
              </label>
              <label className='flex gap-2 text-[15px]'>
                <input type="checkbox" name="" id="" className='accent-black w-5 h-5'/>
                <p>I accept and understand the privacy policy</p>
              </label>
            </div>
            <button className='text-white bg-black w-full py-3 text-lg hover:cursor-pointer hover:opacity-75' type='submit'>Create account</button>
        </div>
    </form>
  )
}

export default RegisterForm