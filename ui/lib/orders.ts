// Shapes of Ordering's order responses as the storefront reads them (docs/01-overview/frontend/ordering.md, "Order").

export type OrderItemDetails = {
    id: string
    productId: string
    productName: string
    unitPrice: number
    quantity: number
}

export type OrderDetails = {
    id: string
    totalPrice: number
    status: string
    createdAt: string
    cancellationReason: string | null
    shippingAddress: { street: string; city: string; state: string; zipCode: string; country: string }
    items: OrderItemDetails[]
}

const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

// Payment charges in USD only, so prices are shown in USD whatever the visitor's locale.
export function formatUsd(value: number): string {
    return usd.format(value)
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function isGuid(value: string): boolean {
    return GUID.test(value)
}
