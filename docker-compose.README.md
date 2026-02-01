# Docker: RabbitMQ → Redis → SQL Server

Order: **1. RabbitMQ** → **2. Redis** → **3. SQL Server**

## Start

```bash
docker-compose up -d
```

## Services

| Service   | Port(s)        | Connection |
|----------|----------------|------------|
| RabbitMQ | 5672 (AMQP), 15672 (UI) | AMQP: `amqp://guest:guest@localhost:5672` |
| Redis    | 6379           | `localhost:6379` |
| SQL Server | 1433        | Server=`localhost,1433`; User=`sa`; Password=`YourStrong@Pass123`; TrustServerCertificate=true |

### RabbitMQ

- **Management UI:** http://localhost:15672  
- **Login:** `guest` / `guest`  
- **Connection string (C#):** `amqp://guest:guest@localhost:5672`  
- Or: Host=localhost, Port=5672, UserName=guest, Password=guest  

### Redis

- **Connection:** `localhost:6379` (no password by default)  
- **StackExchange.Redis:** `localhost:6379` or `localhost:6379,password=...`  

### SQL Server

- **Server:** `localhost,1433` (or `localhost\SQLEXPRESS` if using local SQL)  
- **User:** `sa`  
- **Password:** `YourStrong@Pass123` (change in `docker-compose.yml` for production)  
- **Connection string (C#):**  
  `Server=localhost,1433;User Id=sa;Password=YourStrong@Pass123;TrustServerCertificate=True;`  

To change the SA password, edit `MSSQL_SA_PASSWORD` in `docker-compose.yml` and recreate:

```bash
docker-compose down -v
docker-compose up -d
```

## Stop

```bash
docker-compose down
```

With volumes removed:

```bash
docker-compose down -v
```
