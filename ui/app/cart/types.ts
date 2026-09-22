export type basketItem = {
    productId:string,
    productName:string,
    price:number,
    quantity:number,
    subTotal:number
}

export type basketRes = {
    userId:string,
    items:Array<basketItem>,
    totalPrice:number,
    totalItems:number,
    createdAt:Date,
    lastModifiedAt:Date
}