import Link from "next/link";

export default function PaymentNotFound() {
  return (
    <div className="mx-auto max-w-md py-20 text-center">
      <h1 className="text-2xl font-semibold tracking-tight">Payment not found</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        No payment has this id.
      </p>
      <Link href="/adminPanel/payments" className="mt-4 inline-block text-sm underline">
        Back to payments
      </Link>
    </div>
  );
}
