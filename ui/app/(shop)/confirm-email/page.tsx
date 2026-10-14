import { cookies } from 'next/headers'
import { ConfirmEmailForm, ResendConfirmationForm } from '@/components/Auth/emailForms'
import { REFRESH_COOKIE } from '@/lib/authCookies'
import { LinkSearchParams, readLinkParams } from '@/lib/linkParams'

// Target of the link in the confirmation email (Notification's EmailConfirmation:ConfirmUrlBase).
export default async function ConfirmEmailPage({ searchParams }: { searchParams: LinkSearchParams }) {
  const link = await readLinkParams(searchParams)
  const signedIn = Boolean((await cookies()).get(REFRESH_COOKIE)?.value)

  return (
    <div className='mx-auto flex w-full max-w-md flex-col gap-6 px-4 py-16'>
      <h1 className='text-2xl font-semibold'>Confirm your email address</h1>
      {link ? (
        <>
          <p className='text-gray-600'>Confirm the address to be able to place orders.</p>
          <ConfirmEmailForm userId={link.userId} token={link.token} signedIn={signedIn} />
        </>
      ) : (
        <>
          <p role='alert' className='text-red-600'>
            This link is incomplete. Open the link from the email again, or request a new one.
          </p>
          <ResendConfirmationForm />
        </>
      )}
    </div>
  )
}
