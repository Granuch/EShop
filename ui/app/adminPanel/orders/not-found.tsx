import Link from "next/link";

export default function OrderNotFound() {
  return (
    <div className="mx-auto max-w-md py-20 text-center">
      <h1 className="text-2xl font-semibold tracking-tight">Order not found</h1>
      <p className="mt-1 text-sm text-muted-foreground">No order has this id.</p>
      <Link href="/adminPanel/orders" className="mt-4 inline-block text-sm underline">
        Back to orders
      </Link>
    </div>
  );
}
