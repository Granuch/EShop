'use client'
import Image from 'next/image'
import Link from 'next/link'
import { useRouter } from 'next/navigation'
import React, { useState } from 'react'
import { Problem, problemMessages } from '@/lib/problem'

// Identity answers every refused sign-in with the same 401, so these are the only distinctions worth showing.
function loginError(status: number, data: Problem, retryAfter: string | null): string {
    switch (data?.errorCode) {
        case "Auth.InvalidCredentials": return "Invalid email or password"
        case "Auth.Invalid2FA": return "The authentication code is not correct"
        case "Auth.TooManyAttempts": return data.detail ?? "Too many failed sign-in attempts. Please try again later"
        case "ValidationError": return "Enter a valid email address and your password"
    }
    return problemMessages(status, data, retryAfter, "Sign-in failed. Please try again")[0]
}

function LoginForm() {

    const router = useRouter()
    const [error, setError] = useState<string | null>(null)
    const [pending, setPending] = useState(false)
    const [needsCode, setNeedsCode] = useState(false)

    async function handleSubmit(e: React.SubmitEvent<HTMLFormElement>) {
        e.preventDefault()
        const formData = new FormData(e.currentTarget)
        const body = {
            email: formData.get("email"),
            password: formData.get("password"),
            ...(needsCode ? { twoFactorCode: formData.get("twoFactorCode") } : {})
        }

        setPending(true)
        setError(null)
        try {
            const res = await fetch("/api/auth/login", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(body)
            })
            const data = await res.json().catch(() => null)

            if (!res.ok) {
                setError(loginError(res.status, data, res.headers.get("retry-after")))
                return
            }
            if (data?.requires2FA) {
                setNeedsCode(true)
                return
            }

            router.push("/")
            router.refresh()
        }
        catch {
            setError("The shop could not be reached. Check your connection and try again")
        }
        finally {
            setPending(false)
        }
    }

  return (
    <form onSubmit={handleSubmit}>
        <div className='flex flex-col mt-36 ml-64 items-center gap-8'>
            <h2 className='text-2xl'>Already registered?</h2>
            <input type="email" placeholder='Email' name='email' required autoComplete='email' className='outline-none border-b-2 px-4 py-2 focus:border-black hover:border-gray-300 transition-all w-92'/>
            <input type="password" name='password' placeholder='Password' required autoComplete='current-password' className='outline-none border-b-2 px-4 py-2 focus:border-black hover:border-gray-300 transition-all w-92'/>
            {needsCode && (
                <input type="text" name='twoFactorCode' placeholder='6-digit code from your authenticator app' required
                    inputMode='numeric' pattern='[0-9]{6}' maxLength={6} autoComplete='one-time-code' autoFocus
                    className='outline-none border-b-2 px-4 py-2 focus:border-black hover:border-gray-300 transition-all w-92'/>
            )}
            {error && <p role='alert' className='w-92 text-sm text-red-600'>{error}</p>}
            <button className='text-white text-lg bg-black w-full py-3 hover:cursor-pointer hover:opacity-75 disabled:opacity-50' type='submit' disabled={pending}>
                {pending ? 'Signing in…' : 'Sign in'}
            </button>
            <Link href='/forgot-password' className='text-sm underline underline-offset-4 hover:opacity-75'>Forgot your password?</Link>
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
