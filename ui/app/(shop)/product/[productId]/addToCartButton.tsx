'use client'
import { useTransition } from 'react'
import { ShoppingCart } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { toast } from '@/components/ui/toast'
import { addToCart } from './addToCart'

type AddToCartButtonProps = {
  productId: string,
  productName: string,
  disabled: boolean
}

function AddToCartButton({ productId, productName, disabled }: AddToCartButtonProps) {
  const [pending, startTransition] = useTransition()

  function handleClick() {
    startTransition(async () => {
      try {
        const result = await addToCart(productId)
        if (result.success) {
          toast.add({ title: `${productName} was added to your cart`, type: 'success' })
        } else {
          toast.add({ title: result.message, type: 'error' })
        }
      } catch (err) {
        // The action itself failed to run (network, server error), not Basket refusing the item.
        console.error(err)
        toast.add({ title: 'Could not add the product to your cart. Please try again', type: 'error' })
      }
    })
  }

  return (
    <Button
      type='button'
      size='lg'
      className='h-11 w-full px-6 text-base sm:w-64'
      disabled={disabled || pending}
      onClick={handleClick}
    >
      <ShoppingCart />
      {disabled ? 'Out of stock' : pending ? 'Adding…' : 'Add to cart'}
    </Button>
  )
}

export default AddToCartButton
