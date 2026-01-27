# 📐 C4 Model Diagrams

C4 model для візуалізації архітектури на 4 рівнях: Context, Container, Component, Code.

---

## Level 1: System Context

Показує E-Shop систему та її взаємодію з користувачами і зовнішніми системами.

```
┌─────────────────────────────────────────────────────────┐
│                    System Context                        │
├─────────────────────────────────────────────────────────┤
│                                                          │
│   ┌──────────┐                                          │
│   │  Customer│                                          │
│   │  (User)  │                                          │
│   └────┬─────┘                                          │
│        │                                                 │
│        │ Uses                                            │
│        ▼                                                 │
│   ┌──────────────────────────────────┐                  │
│   │     E-Shop Platform              │                  │
│   │  (Microservices System)          │                  │
│   │                                  │                  │
│   │  Browse products, Place orders,  │                  │
│   │  Track deliveries                │                  │
│   └──────────┬──────────┬────────────┘                  │
│              │          │                               │
│              │          │ Uses                          │
│              │          ▼                               │
│              │   ┌─────────────┐                        │
│              │   │   Stripe    │                        │
│              │   │  (Payment)  │                        │
│              │   └─────────────┘                        │
│              │                                           │
│              │ Sends emails                             │
│              ▼                                           │
│       ┌─────────────┐                                   │
│       │  SendGrid   │                                   │
│       │   (Email)   │                                   │
│       └─────────────┘                                   │
│                                                          │
│   ┌──────────┐                                          │
│   │  Admin   │                                          │
│   │  (User)  │                                          │
│   └────┬─────┘                                          │
│        │                                                 │
│        │ Manages                                         │
│        ▼                                                 │
│   ┌──────────────────────────────────┐                  │
│   │    Admin Panel                   │                  │
│   │  (Web Application)               │                  │
│   └──────────────────────────────────┘                  │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

**Key:**
- **Customer**: End user browsing and purchasing products
- **Admin**: Internal user managing catalog, orders
- **E-Shop Platform**: Our microservices system
- **Stripe**: External payment processor
- **SendGrid**: External email delivery service

---

## Level 2: Container Diagram

Показує високорівневу технічну архітектуру (containers = deployable units).

```
┌───────────────────────────────────────────────────────────────┐
│                     Container Diagram                          │
├───────────────────────────────────────────────────────────────┤
│                                                                │
│  ┌──────────┐                                                 │
│  │ Customer │                                                 │
│  └────┬─────┘                                                 │
│       │ HTTPS                                                 │
│       ▼                                                        │
│  ┌──────────────────────────────────┐                         │
│  │     Web Application              │                         │
│  │  (React SPA)                     │                         │
│  │  [TypeScript, Vite]              │                         │
│  └────────────┬─────────────────────┘                         │
│               │ HTTPS/JSON                                    │
│               ▼                                                │
│  ┌──────────────────────────────────┐                         │
│  │     API Gateway                  │                         │
│  │  (YARP Reverse Proxy)            │                         │
│  │  [ASP.NET Core]                  │                         │
│  └────┬────┬────┬────┬────┬─────────┘                         │
│       │    │    │    │    │                                   │
│  ┌────▼─┐ ┌▼───┐ ┌──▼┐ ┌─▼──┐ ┌───▼───┐                      │
│  │Identity│Catalog│Basket│Order│Payment│                      │
│  │Service││Service│Serv.││Serv.││Service│                     │
│  │[.NET] ││[.NET]│[.NET]││[.NET]││[.NET]│                     │
│  └───┬───┘└──┬──┘└──┬──┘└──┬──┘└───┬───┘                      │
│      │       │       │      │       │                         │
│      │       │       │      │       │                         │
│  ┌───▼───────▼───────▼──────▼───────▼───┐                     │
│  │        Message Broker                │                     │
│  │         (RabbitMQ)                   │                     │
│  └──────────────────────────────────────┘                     │
│                                                                │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐                    │
│  │PostgreSQL│  │  Redis   │  │  Seq     │                    │
│  │(Database)│  │ (Cache)  │  │ (Logs)   │                    │
│  └──────────┘  └──────────┘  └──────────┘                    │
│                                                                │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐                    │
│  │Prometheus│  │  Jaeger  │  │ Grafana  │                    │
│  │(Metrics) │  │(Tracing) │  │(Dashbds) │                    │
│  └──────────┘  └──────────┘  └──────────┘                    │
│                                                                │
└───────────────────────────────────────────────────────────────┘
```

**Containers:**

| Container | Technology | Purpose |
|-----------|------------|---------|
| **Web Application** | React + TypeScript | User interface |
| **API Gateway** | YARP (ASP.NET Core) | Routing, auth, rate limiting |
| **Identity Service** | ASP.NET Core | Authentication, JWT |
| **Catalog Service** | ASP.NET Core | Product management |
| **Basket Service** | ASP.NET Core | Shopping cart |
| **Ordering Service** | ASP.NET Core | Order processing |
| **Payment Service** | ASP.NET Core | Payment integration |
| **Message Broker** | RabbitMQ | Async communication |
| **Database** | PostgreSQL | Data persistence |
| **Cache** | Redis | Distributed cache |
| **Logging** | Seq | Centralized logs |
| **Tracing** | Jaeger | Distributed tracing |
| **Metrics** | Prometheus + Grafana | Monitoring |

---

## Level 3: Component Diagram (Catalog Service)

Показує внутрішню структуру Catalog Service (Clean Architecture).

```
┌─────────────────────────────────────────────────────────┐
│              Catalog Service Components                  │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  ┌────────────────────────────────────────────────┐     │
│  │              API Layer                         │     │
│  │  ┌──────────────┐  ┌──────────────┐           │     │
│  │  │Products      │  │Categories    │           │     │
│  │  │Controller    │  │Controller    │           │     │
│  │  └──────┬───────┘  └──────┬───────┘           │     │
│  └─────────┼──────────────────┼───────────────────┘     │
│            │                  │                         │
│            ▼                  ▼                         │
│  ┌─────────────────────────────────────────────────┐    │
│  │          Application Layer (CQRS)               │    │
│  │  ┌─────────────┐       ┌──────────────┐        │    │
│  │  │  Commands   │       │   Queries    │        │    │
│  │  │  ─────────  │       │  ──────────  │        │    │
│  │  │CreateProduct│       │GetProducts   │        │    │
│  │  │UpdateProduct│       │GetProductById│        │    │
│  │  │DeleteProduct│       │SearchProducts│        │    │
│  │  └──────┬──────┘       └──────┬───────┘        │    │
│  └─────────┼──────────────────────┼──────────────┘     │
│            │                      │                     │
│            ▼                      ▼                     │
│  ┌──────────────────────────────────────────────┐      │
│  │           Domain Layer                       │      │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐   │      │
│  │  │ Product  │  │ Category │  │  Money   │   │      │
│  │  │(Aggregate│  │ (Entity) │  │ (Value   │   │      │
│  │  │  Root)   │  │          │  │  Object) │   │      │
│  │  └──────────┘  └──────────┘  └──────────┘   │      │
│  │                                              │      │
│  │  ┌───────────────────────────────────┐      │      │
│  │  │       Domain Events               │      │      │
│  │  │  ProductCreated                   │      │      │
│  │  │  ProductPriceChanged              │      │      │
│  │  │  ProductPublished                 │      │      │
│  │  └───────────────────────────────────┘      │      │
│  └──────────────┬───────────────────────────────┘      │
│                 │                                      │
│                 ▼                                      │
│  ┌──────────────────────────────────────────────┐     │
│  │        Infrastructure Layer                  │     │
│  │  ┌──────────────┐  ┌──────────────┐         │     │
│  │  │CatalogDb     │  │Product       │         │     │
│  │  │Context       │  │Repository    │         │     │
│  │  │(EF Core)     │  │(with Cache)  │         │     │
│  │  └──────┬───────┘  └──────┬───────┘         │     │
│  └─────────┼──────────────────┼──────────────────┘    │
│            │                  │                       │
│            ▼                  ▼                       │
│     ┌────────────┐     ┌────────────┐               │
│     │ PostgreSQL │     │   Redis    │               │
│     └────────────┘     └────────────┘               │
│                                                      │
└──────────────────────────────────────────────────────┘
```

**Components:**

| Layer | Component | Responsibility |
|-------|-----------|----------------|
| **API** | ProductsController | HTTP endpoints |
| **Application** | CreateProductCommand | Business use case |
| **Application** | GetProductsQuery | Data retrieval |
| **Domain** | Product (Aggregate) | Business logic |
| **Domain** | Money (Value Object) | Price representation |
| **Infrastructure** | CatalogDbContext | Database access |
| **Infrastructure** | ProductRepository | Data persistence |

---

## Level 4: Code Diagram (Product Aggregate)

Детальна UML діаграма класу Product.

```
┌─────────────────────────────────────────────────────┐
│                Product (Aggregate Root)              │
├─────────────────────────────────────────────────────┤
│ - Id: Guid                                          │
│ - Name: string                                      │
│ - Description: string                               │
│ - Sku: string                                       │
│ - Price: Money                                      │
│ - DiscountPrice: Money?                             │
│ - StockQuantity: int                                │
│ - CategoryId: Guid                                  │
│ - Status: ProductStatus                             │
│ - Images: List<ProductImage>                        │
│ - CreatedAt: DateTime                               │
│ - CreatedBy: string                                 │
│ - DomainEvents: List<IDomainEvent>                  │
├─────────────────────────────────────────────────────┤
│ + Create(name, sku, price, ...): Product           │
│ + UpdatePrice(newPrice: Money): void               │
│ + UpdateStock(quantity: int): void                 │
│ + Publish(): void                                  │
│ + AddImage(url, altText): void                     │
│ + AddDomainEvent(event: IDomainEvent): void        │
│ - ValidateName(name: string): void                 │
│ - ValidateSku(sku: string): void                   │
└─────────────────────────────────────────────────────┘
         │
         │ contains
         ▼
┌─────────────────────────────────────────────────────┐
│              Money (Value Object)                    │
├─────────────────────────────────────────────────────┤
│ + Amount: decimal                                   │
│ + Currency: string                                  │
├─────────────────────────────────────────────────────┤
│ + Add(other: Money): Money                         │
│ + Subtract(other: Money): Money                    │
│ + Equals(other: Money): bool                       │
└─────────────────────────────────────────────────────┘
```

---

## Deployment Diagram (Kubernetes)

Фізичне розгортання в production.

```
┌─────────────────────────────────────────────────────────┐
│                  Kubernetes Cluster                      │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  ┌──────────────────────────────────────────────────┐   │
│  │              Ingress Controller                  │   │
│  │         (NGINX / Azure App Gateway)              │   │
│  └────────────────┬─────────────────────────────────┘   │
│                   │                                      │
│                   ▼                                      │
│  ┌──────────────────────────────────────────────────┐   │
│  │            API Gateway Service                   │   │
│  │  ┌─────┐  ┌─────┐  ┌─────┐                      │   │
│  │  │ Pod │  │ Pod │  │ Pod │  (3 replicas)        │   │
│  │  └─────┘  └─────┘  └─────┘                      │   │
│  └────┬─────┬─────┬─────┬─────┬──────────────────┘    │
│       │     │     │     │     │                       │
│  ┌────▼─┐ ┌─▼───┐ ┌───▼┐ ┌──▼─┐ ┌────▼────┐          │
│  │Ident.│ │Cata-│ │Bask-│ │Ord-│ │Payment  │          │
│  │Svc   │ │log  │ │et   │ │er  │ │Service  │          │
│  │      │ │Svc  │ │Svc  │ │Svc │ │         │          │
│  │3 pods│ │3pods│ │2pods│ │3pds│ │2 pods   │          │
│  └──────┘ └─────┘ └─────┘ └────┘ └─────────┘          │
│                                                         │
│  ┌──────────────────────────────────────────────────┐  │
│  │              Stateful Services                   │  │
│  │  ┌─────────────┐  ┌─────────────┐               │  │
│  │  │ PostgreSQL  │  │  RabbitMQ   │               │  │
│  │  │ StatefulSet │  │ StatefulSet │               │  │
│  │  │  (3 nodes)  │  │  (3 nodes)  │               │  │
│  │  └─────────────┘  └─────────────┘               │  │
│  │                                                  │  │
│  │  ┌─────────────┐  ┌─────────────┐               │  │
│  │  │   Redis     │  │     Seq     │               │  │
│  │  │ StatefulSet │  │ Deployment  │               │  │
│  │  │  (3 nodes)  │  │  (1 pod)    │               │  │
│  │  └─────────────┘  └─────────────┘               │  │
│  └──────────────────────────────────────────────────┘  │
│                                                         │
│  ┌──────────────────────────────────────────────────┐  │
│  │           Persistent Volumes                     │  │
│  │  (Azure Disk / AWS EBS)                          │  │
│  └──────────────────────────────────────────────────┘  │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

**Resources:**

| Service | Replicas | CPU Request | Memory Request |
|---------|----------|-------------|----------------|
| API Gateway | 3 | 250m | 512Mi |
| Identity | 3 | 250m | 512Mi |
| Catalog | 3 | 250m | 512Mi |
| Basket | 2 | 250m | 256Mi |
| Ordering | 3 | 250m | 512Mi |
| Payment | 2 | 250m | 256Mi |
| PostgreSQL | 3 | 500m | 2Gi |
| RabbitMQ | 3 | 250m | 1Gi |
| Redis | 3 | 250m | 512Mi |

---

## Sequence Diagram: Create Order Flow

```
Customer  WebApp  Gateway  Basket  Ordering  Payment  RabbitMQ  Email
   │         │       │        │        │        │         │        │
   │─Checkout──────►│        │        │        │         │        │
   │         │       │        │        │        │         │        │
   │         │       │──POST /basket/checkout─►│         │        │
   │         │       │        │        │        │         │        │
   │         │       │        │──Get Basket────►│         │        │
   │         │       │        │◄────Items───────│         │        │
   │         │       │        │        │        │         │        │
   │         │       │        │─Publish: BasketCheckedOut─►        │
   │         │       │        │        │        │         │        │
   │         │       │        │        │◄─Consume Event──┘        │
   │         │       │        │        │        │         │        │
   │         │       │        │        │─Create Order────►│        │
   │         │       │        │        │◄─Order Created──┘        │
   │         │       │        │        │        │         │        │
   │         │       │        │        │─Publish: OrderCreated────►│
   │         │       │        │        │        │         │        │
   │         │       │        │        │        │◄─Consume Event──┘│
   │         │       │        │        │        │         │        │
   │         │       │        │        │        │─Process Payment─►│
   │         │       │        │        │        │◄─Success────────┘│
   │         │       │        │        │        │         │        │
   │         │       │        │        │        │─Publish: PaymentSuccess─►│
   │         │       │        │        │◄───────┘         │        │
   │         │       │        │        │                  │        │
   │         │       │        │        │─Update Order Status       │
   │         │       │        │        │                  │        │
   │         │       │        │        │──────────────────┘        │
   │         │       │        │        │                           │
   │         │       │◄─200 OK (OrderId)─────────────────────────►│
   │         │◄──────┘        │        │                  │        │
   │◄────────┘                │        │                  │        │
   │                          │        │                  │        │
```

---

## Data Flow Diagram

```
┌─────────────────────────────────────────────────────────┐
│                    Data Flow                             │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  User ───► Web App ───► API Gateway                     │
│                            │                             │
│                            ├──► Identity Service         │
│                            │      └──► PostgreSQL        │
│                            │                             │
│                            ├──► Catalog Service          │
│                            │      ├──► PostgreSQL        │
│                            │      └──► Redis (Cache)     │
│                            │                             │
│                            ├──► Basket Service           │
│                            │      └──► Redis             │
│                            │                             │
│                            ├──► Ordering Service         │
│                            │      ├──► PostgreSQL        │
│                            │      └──► RabbitMQ          │
│                            │                             │
│                            └──► Payment Service          │
│                                   ├──► PostgreSQL        │
│                                   ├──► Stripe API        │
│                                   └──► RabbitMQ          │
│                                                          │
│  All Services ───► Seq (Logging)                        │
│  All Services ───► Prometheus (Metrics)                 │
│  All Services ───► Jaeger (Tracing)                     │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

---

## Tools for Creating C4 Diagrams

### 1. Structurizr (Recommended)

```csharp
// C4 model as code

var workspace = new Workspace("E-Shop", "Microservices e-commerce platform");
var model = workspace.Model;

var customer = model.AddPerson("Customer", "E-Shop customer");
var eshop = model.AddSoftwareSystem("E-Shop", "E-commerce platform");

customer.Uses(eshop, "Browse products, Place orders");

var webApp = eshop.AddContainer("Web Application", "React SPA", "TypeScript");
var apiGateway = eshop.AddContainer("API Gateway", "Reverse proxy", "YARP");

customer.Uses(webApp, "Uses", "HTTPS");
webApp.Uses(apiGateway, "Makes API calls", "JSON/HTTPS");
```

### 2. PlantUML

```plantuml
@startuml
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Container.puml

Person(customer, "Customer", "E-Shop user")
System_Boundary(c1, "E-Shop Platform") {
    Container(web, "Web App", "React", "User interface")
    Container(gateway, "API Gateway", "YARP", "Routing")
    Container(identity, "Identity Service", ".NET", "Auth")
}

Rel(customer, web, "Uses", "HTTPS")
Rel(web, gateway, "Calls", "JSON/HTTPS")
Rel(gateway, identity, "Routes to", "HTTP")
@enduml
```

### 3. Draw.io / Excalidraw

Візуальні редактори для швидкого створення діаграм.

---

## References

- [C4 Model](https://c4model.com/)
- [Structurizr](https://structurizr.com/)
- [PlantUML C4](https://github.com/plantuml-stdlib/C4-PlantUML)

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
