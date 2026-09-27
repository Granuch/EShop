export type itemData = {
    id:string,
    name:string,
    description:string | null,
    sku:string,
    price:number,
    stockQuantity:number,
    mainImageUrl:string,
    createdAt:Date
}

export type itemDatabyId = {
    id:string,
    name:string,
    description:string | null,
    sku:string,
    price:number,
    stockQuantity:number,
    mainImageUrl:string,
    createdAt:Date
    images: Array<image>,
    Attributes: Array<object>
}

type image = {
    id:string,
    url:string,
    altText:string,
    displayOrder:number,
    isMain:boolean
}