'use client'
import { useRouter } from 'next/navigation'
import React, { useState } from 'react'
import { problemMessages } from '@/lib/problem'

const inputClass = 'outline-none border-b-2 px-4 py-2 focus:border-black hover:border-gray-300 transition-all w-92'

function RegisterForm() {
  const router = useRouter()
  const [errors, setErrors] = useState<string[]>([])
  const [pending, setPending] = useState(false)

  async function handleSubmit(e:React.SubmitEvent<HTMLFormElement>) {
    e.preventDefault()
    const formData = new FormData(e.currentTarget)
    const email = formData.get("email")
    const password = formData.get("password")

    setPending(true)
    setErrors([])
    try {
      const reg = await fetch("/api/auth/register", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          email,
          password,
          firstName: formData.get("firstName"),
          lastName: formData.get("lastName")
        })
      })
      if (!reg.ok) {
        const data = await reg.json().catch(() => null)
        setErrors(problemMessages(reg.status, data, reg.headers.get("retry-after"), "The account could not be created"))
        return
      }

      // The account exists now; signing in is a convenience, so a failure here only asks the user to sign in.
      const log = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, password })
      })
      if (!log.ok) {
        setErrors(["Your account was created. Sign in with your email and password."])
        return
      }

      // The account page says a confirmation email is on its way, and can send another.
      router.push("/account")
      router.refresh()
    }
    catch {
      setErrors(["The shop could not be reached. Check your connection and try again"])
    }
    finally {
      setPending(false)
    }
  }

  return (
    <form onSubmit={handleSubmit}>
        <div className='flex flex-col mt-35 mr-64 items-start gap-8'>
            <h2 className='text-2xl w-full text-center'>Is this your first visit?</h2>
            <input type="text" placeholder='First name' name='firstName' required maxLength={50} autoComplete='given-name' className={inputClass}/>
            <input type="text" placeholder='Last name' name='lastName' required maxLength={50} autoComplete='family-name' className={inputClass}/>
            <input type="email"  placeholder='Email' name='email' required maxLength={256} autoComplete='email' className={inputClass}/>
            <input type="password" placeholder='Password' name='password' required minLength={8} maxLength={100} autoComplete='new-password' className={inputClass}/>
            <p className='w-92 text-xs text-gray-500'>At least 8 characters, with an uppercase and a lowercase letter, a digit and a symbol.</p>
            <div className='flex flex-col gap-3'>
              <label className='flex gap-2 text-[15px]'>
                <input type="checkbox" name="newsletter" className='accent-black w-5 h-5'/>
                <p>Yes, I would like to sign up for the newsletter</p>
              </label>
              <label className='flex gap-2 text-[15px]'>
                <input type="checkbox" name="privacy" className='accent-black w-5 h-5'/>
                <p>I accept and understand the privacy policy</p>
              </label>
            </div>
            {errors.length > 0 && (
              <ul role='alert' className='w-92 list-disc pl-5 text-sm text-red-600'>
                {errors.map((message) => <li key={message}>{message}</li>)}
              </ul>
            )}
            <button className='text-white bg-black w-full py-3 text-lg hover:cursor-pointer hover:opacity-75 disabled:opacity-50' type='submit' disabled={pending}>
              {pending ? 'Creating account…' : 'Create account'}
            </button>
        </div>
    </form>
  )
}

export default RegisterForm
