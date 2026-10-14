import Link from 'next/link'
import { ForgotPasswordForm } from '@/components/Auth/emailForms'

export default function ForgotPasswordPage() {
  return (
    <div className='mx-auto flex w-full max-w-md flex-col gap-6 px-4 py-16'>
      <h1 className='text-2xl font-semibold'>Forgot your password?</h1>
      <p className='text-gray-600'>Enter the email address of your account and we will send you a link to choose a new password.</p>
      <ForgotPasswordForm />
      <Link href='/autorization' className='text-sm underline underline-offset-4'>Back to sign in</Link>
    </div>
  )
}
