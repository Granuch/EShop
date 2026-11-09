'use client'
import Link from 'next/link'
import { useActionState } from 'react'
import { FlowState, confirmEmail, forgotPassword, resendConfirmation, resetPassword } from '@/lib/authActions'

const inputClass = 'w-full outline-none border-b-2 px-4 py-2 focus:border-black hover:border-gray-300 transition-all'
const buttonClass = 'w-full bg-black py-3 text-lg text-white hover:cursor-pointer hover:opacity-75 disabled:opacity-50'

function Messages({ state }: { state: FlowState }) {
  if (!state || state.messages.length === 0) return null
  return (
    <ul role={state.ok ? 'status' : 'alert'} className={`text-sm ${state.ok ? 'text-green-700' : 'text-red-600'}`}>
      {state.messages.map((message) => <li key={message}>{message}</li>)}
    </ul>
  )
}

// The user clicks to confirm rather than the page confirming on load, so a mail scanner that opens the link
// confirms nothing.
export function ConfirmEmailForm({ userId, token, signedIn }: { userId: string; token: string; signedIn: boolean }) {
  const [state, action, pending] = useActionState(confirmEmail, null)

  if (state?.ok) {
    return (
      <div className='flex flex-col gap-4'>
        <Messages state={state} />
        <Link href={signedIn ? '/' : '/autorization'} className={`${buttonClass} text-center`}>
          {signedIn ? 'Continue shopping' : 'Sign in'}
        </Link>
      </div>
    )
  }

  return (
    <div className='flex flex-col gap-6'>
      <form action={action} className='flex flex-col gap-4'>
        <input type='hidden' name='userId' value={userId} />
        <input type='hidden' name='token' value={token} />
        <button type='submit' className={buttonClass} disabled={pending}>
          {pending ? 'Confirming…' : 'Confirm my email address'}
        </button>
        <Messages state={state} />
      </form>
      {state && !state.ok && <ResendConfirmationForm />}
    </div>
  )
}

export function ResendConfirmationForm({ email }: { email?: string }) {
  const [state, action, pending] = useActionState(resendConfirmation, null)

  return (
    <form action={action} className='flex flex-col gap-4'>
      {email
        ? <input type='hidden' name='email' value={email} />
        : <input type='email' name='email' required placeholder='Your email address' autoComplete='email' className={inputClass} />}
      <button type='submit' className={buttonClass} disabled={pending}>
        {pending ? 'Sending…' : 'Send a new confirmation link'}
      </button>
      <Messages state={state} />
    </form>
  )
}

export function ForgotPasswordForm() {
  const [state, action, pending] = useActionState(forgotPassword, null)

  return (
    <form action={action} className='flex flex-col gap-4'>
      <input type='email' name='email' required placeholder='Your email address' autoComplete='email' className={inputClass} />
      <button type='submit' className={buttonClass} disabled={pending}>
        {pending ? 'Sending…' : 'Send me a reset link'}
      </button>
      <Messages state={state} />
    </form>
  )
}

export function ResetPasswordForm({ userId, token }: { userId: string; token: string }) {
  const [state, action, pending] = useActionState(resetPassword, null)

  if (state?.ok) {
    return (
      <div className='flex flex-col gap-4'>
        <Messages state={state} />
        <Link href='/autorization' className={`${buttonClass} text-center`}>Sign in</Link>
      </div>
    )
  }

  return (
    <form action={action} className='flex flex-col gap-4'>
      <input type='hidden' name='userId' value={userId} />
      <input type='hidden' name='token' value={token} />
      <input type='password' name='newPassword' required minLength={8} placeholder='New password'
        autoComplete='new-password' className={inputClass} />
      <input type='password' name='confirmPassword' required minLength={8} placeholder='Repeat the new password'
        autoComplete='new-password' className={inputClass} />
      <p className='text-xs text-gray-500'>At least 8 characters, with an uppercase and a lowercase letter, a digit and a symbol.</p>
      <button type='submit' className={buttonClass} disabled={pending}>
        {pending ? 'Saving…' : 'Set new password'}
      </button>
      <Messages state={state} />
      {state && !state.ok && (
        <Link href='/forgot-password' className='text-sm underline underline-offset-4'>Request a new reset link</Link>
      )}
    </form>
  )
}
