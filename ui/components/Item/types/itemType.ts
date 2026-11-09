export type ProductStatus = 'Draft' | 'Active' | 'Discontinued'

export type itemData = {
    id:string,
    name:string,
    description:string | null,
    sku:string,
    price:number,
    discountPrice:number | null,
    stockQuantity:number,
    status:ProductStatus,
    categoryId:string,
    mainImageUrl:string | null,
    createdAt:Date
}

export type itemDatabyId = itemData & {
    images: Array<image>,
    attributes: Array<attribute>
}

type image = {
    id:string,
    url:string,
    altText:string | null,
    displayOrder:number,
    isMain:boolean
}

type attribute = {
    id:string,
    name:string,
    value:string
}
