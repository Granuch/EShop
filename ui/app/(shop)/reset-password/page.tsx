import Link from 'next/link'
import { ResetPasswordForm } from '@/components/Auth/emailForms'
import { LinkSearchParams, readLinkParams } from '@/lib/linkParams'

// Target of the link in the password-reset email (Notification's PasswordReset:ResetUrlBase). An account an admin
// created is completed here too: its first email is a reset link.
export default async function ResetPasswordPage({ searchParams }: { searchParams: LinkSearchParams }) {
  const link = await readLinkParams(searchParams)

  return (
    <div className='mx-auto flex w-full max-w-md flex-col gap-6 px-4 py-16'>
      <h1 className='text-2xl font-semibold'>Choose a new password</h1>
      {link ? (
        <ResetPasswordForm userId={link.userId} token={link.token} />
      ) : (
        <>
          <p role='alert' className='text-red-600'>
            This link is incomplete. Open the link from the email again, or request a new one.
          </p>
          <Link href='/forgot-password' className='underline underline-offset-4'>Request a new reset link</Link>
        </>
      )}
    </div>
  )
}
