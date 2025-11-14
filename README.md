# ABC Retail Management Web Application

## Student Information
- **Student Number:** ST10439147
- **Student Name:** Dillon Rinkwest
- **Course Code:** CLDV6212
- **POE Part:** 3 - Enhanced with Authentication & Admin Management

## Overview
A comprehensive ASP.NET Core MVC web application for ABC Retail that provides complete order, customer, and product management functionality. The application features a robust authentication system, role-based access control, and seamless integration with Azure cloud services including SQL Database, Table Storage, Queue Storage, Blob Storage, and File Share services.

The solution implements a hybrid architecture using:
- **SQL Server (Azure SQL Database)** via ADO.NET for user authentication and credentials
- **Azure Table Storage** for customer profiles, products, and orders
- **Azure Functions** for serverless operations and API endpoints
- **Azure Queue Storage** for asynchronous message processing
- **Azure Blob Storage** for file storage
- **Azure File Share** for shared file access

## Features

### Authentication & Authorization
- **User Registration & Login** with secure password hashing (SHA256)
- **Role-Based Access Control** (Admin/Customer roles)
- **Cookie-based Authentication** with persistent login option
- **Account Management** including profile viewing and last login tracking
- **Access Control** with authorization policies and redirects

### Order Management
- View all orders with filtering by status
- Create, edit, and delete orders
- Order status tracking (Pending, Processed, Cancelled)
- Detailed order views with customer and product information
- Admin order oversight and status updates

### Customer Management
- Customer profile creation and management
- Integration with user authentication system
- Customer details display in orders and profiles
- CRUD operations via Azure Functions

### Product Management
- Product catalog management
- Product details and inventory tracking
- Integration with order system

### Admin Dashboard
- System statistics overview (orders, products, customers)
- Order status breakdown (Pending, Processed)
- Quick access to order management
- Admin-only access with role authorization

### Queue & Storage
- Queue message management for asynchronous operations
- Blob storage integration for file uploads
- File share support for shared documents

## Technologies

### Backend
- **ASP.NET Core MVC** (.NET 8, C# 12)
- **ADO.NET** for SQL database operations
- **Azure Functions** (.NET 8, Isolated Worker)
- **Entity Framework Core** for data access patterns

### Frontend
- **Razor Views** with MVC pattern
- **Bootstrap 5** for responsive UI
- **FontAwesome** for icons
- **jQuery** for client-side interactions

### Azure Services
- **Azure SQL Database** - User authentication and credentials
- **Azure Table Storage** - Customer profiles, products, orders
- **Azure Queue Storage** - Asynchronous message processing
- **Azure Blob Storage** - File storage
- **Azure File Share** - Shared file access
- **Azure Functions** - Serverless API endpoints

### Security
- **Cookie Authentication** with configurable expiration
- **Anti-Forgery Tokens** for CSRF protection
- **Password Hashing** using SHA256
- **Parameterized Queries** for SQL injection prevention
- **Role-Based Authorization** for access control

## Project Architecture

### Controllers
- **AccountController.cs** - Handles authentication, registration, login/logout, and profile management
- **AdminController.cs** - Admin dashboard, order management, and status updates
- **OrderController.cs** - Order CRUD operations (Customer-facing)
- **ProductController.cs** - Product management operations
- **CustomerController.cs** - Customer management operations

### Services
- **AuthenticationService.cs** - User authentication, registration, password hashing, and account management using ADO.NET
- **DatabaseHelper.cs** - Low-level ADO.NET helper for SQL operations (queries, non-queries, scalars, stored procedures)
- **TableService.cs** - Azure Table Storage operations for customers, products, and orders
- **QueueService.cs** - Azure Queue Storage message handling
- **BlobService.cs** - Azure Blob Storage file operations
- **FileService.cs** - Azure File Share operations

### Azure Functions
- **FileUploadFunction.cs** - File upload/download via Azure File Share
- **CustomerFunction.cs** - Customer CRUD operations via Azure Table Storage
- **ProductFunction.cs** - Product management endpoints
- **OrderFunction.cs** - Order processing endpoints

### Models
- **User** - User authentication credentials (SQL Database)
- **Customer** - Customer profile information (Table Storage)
- **Product** - Product catalog data (Table Storage)
- **Order** - Order details and status (Table Storage)
- **LoginViewModel** - Login form data
- **RegisterViewModel** - Registration form data

## NuGet Packages

### Web Application
- **System.Data.SqlClient** - ADO.NET SQL Server provider
- **Azure.Data.Tables** - Azure Table Storage client
- **Azure.Storage.Queues** - Azure Queue Storage client
- **Azure.Storage.Blobs** - Azure Blob Storage client
- **Azure.Storage.Files.Shares** - Azure File Share client
- **Microsoft.Extensions.Configuration** - Configuration management
- **Microsoft.Extensions.Logging** - Logging infrastructure
- **Microsoft.AspNetCore.Authentication.Cookies** - Cookie authentication
- **System.Text.Json** - JSON serialization

### Azure Functions Project
- **Azure.Data.Tables** - Table Storage operations
- **Azure.Storage.Blobs** - Blob Storage operations
- **Azure.Storage.Queues** - Queue Storage operations
- **Azure.Storage.Files.Shares** - File Share operations
- **Microsoft.Azure.Functions.Worker** - Isolated worker SDK
- **Microsoft.Azure.Functions.Worker.Extensions.Http** - HTTP trigger support
- **Microsoft.ApplicationInsights** - Application monitoring
- **Newtonsoft.Json** - JSON serialization

## Getting Started

### Prerequisites
- .NET 8 SDK or later
- Visual Studio 2022 or later
- Azure subscription with the following services configured:
  - Azure SQL Database
  - Azure Storage Account (Table, Blob, Queue, File Share)
  - Azure Functions App Service

### Configuration

1. **Clone the repository:**
   ```bash
   git clone <repository-url>
   cd ST10439147_CLDV6212_POE
   ```

2. **Configure Connection Strings** in `appsettings.json`:
   ```json
   {
     "ConnectionStrings": {
       "AzureSQL": "Server=tcp:your-server.database.windows.net;Database=your-db;User ID=your-user;Password=your-password;"
     },
     "AzureStorage": {
       "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=your-account;AccountKey=your-key;"
     },
     "AzureFunctions": {
       "BaseUrl": "https://your-functions-app.azurewebsites.net/api",
       "FunctionKey": "your-function-key"
     }
   }
   ```

3. **Database Setup:**
   - Create the SQL Database using the provided schema
   - Ensure the Users table exists with proper structure
   - Configure firewall rules to allow application access

4. **Azure Storage Setup:**
   - Create storage account with Table, Blob, Queue, and File Share enabled
   - Create required tables: Customer, Product, Order
   - Configure CORS if accessing from web client

5. **Restore NuGet Packages:**
   - Right-click solution → Restore NuGet Packages
   - Or run: `dotnet restore`

6. **Build the Solution:**
   - Build → Build Solution (Ctrl+Shift+B)
   - Or run: `dotnet build`

### Running the Application

#### Option 1: Run Both Projects Simultaneously (Recommended)
1. Right-click on the solution in Visual Studio
2. Select **Properties** or **Configure Startup Projects**
3. Choose **Multiple startup projects**
4. Set both projects to **Start**:
   - ST10439147_CLDV6212_POE (Web App)
   - ST10439147_CLDV6212_POE.Functions (Functions)
5. Press **F5** or click **Start Debugging**

#### Option 2: Run Individually
- **Web Application:** Set as startup project and press F5
- **Azure Functions:** Set Functions project as startup project and press F5

### Default Admin Account
After database setup, create an admin user manually in the SQL database or register through the UI and update the Role to "Admin" in the Users table.

## Usage Guide

### For Customers
1. **Register:** Navigate to `/Account/Register` to create a new account
2. **Login:** Use your credentials at `/Account/Login`
3. **Browse Products:** View available products in the catalog
4. **Place Orders:** Select products and place orders
5. **View Profile:** Access your profile at `/Account/Profile`
6. **Track Orders:** View your order history and status

### For Administrators
1. **Login:** Use admin credentials at `/Account/Login`
2. **Dashboard:** View system statistics at `/Admin/Index`
3. **Manage Orders:** 
   - View all orders at `/Admin/Orders`
   - Filter by status (Pending, Processed, Cancelled)
   - Update order status
   - Process orders with one click
4. **View Details:** Access detailed order information with customer and product context
5. **Monitor System:** Track order counts, pending items, and processed orders

### API Endpoints (Azure Functions)
- **GET** `/api/customers/{partitionKey}/{rowKey}` - Get customer by ID
- **POST** `/api/customers` - Create new customer
- **PUT** `/api/customers` - Update customer
- **DELETE** `/api/customers/{partitionKey}/{rowKey}` - Delete customer
- **GET** `/api/files/{fileName}` - Download file
- **POST** `/api/files` - Upload file

## Security Considerations

### Implemented Security Features
- Password hashing using SHA256 (consider upgrading to bcrypt or Argon2 for production)
- Parameterized SQL queries to prevent SQL injection
- Anti-forgery tokens on all forms to prevent CSRF attacks
- Role-based authorization for admin functions
- Cookie authentication with configurable session timeouts
- HTTPS enforcement (configure in production)
- Input validation on all user inputs

### Recommended Enhancements for Production
- Implement bcrypt or Argon2 for password hashing
- Add rate limiting for login attempts
- Implement two-factor authentication (2FA)
- Add email verification for registration
- Implement password reset functionality
- Use Azure Key Vault for sensitive configuration
- Enable Application Insights for monitoring
- Implement comprehensive audit logging

## Troubleshooting

### Common Issues

**Database Connection Errors:**
- Verify connection string in appsettings.json
- Check Azure SQL firewall rules
- Ensure database exists and is accessible

**Authentication Issues:**
- Clear browser cookies
- Verify User table exists in database
- Check role assignments in Users table

**Azure Storage Errors:**
- Validate storage connection string
- Ensure tables/containers exist
- Check storage account access keys

**Functions Not Running:**
- Verify Functions project is set to start
- Check Azure Functions configuration
- Ensure function keys are correct in appsettings.json

## Project Structure
```
ST10439147_CLDV6212_POE/
├── Controllers/
│   ├── AccountController.cs
│   ├── AdminController.cs
│   ├── OrderController.cs
│   ├── ProductController.cs
│   └── CustomerController.cs
├── Services/
│   ├── AuthenticationService.cs
│   ├── DatabaseHelper.cs
│   ├── TableService.cs
│   ├── QueueService.cs
│   └── BlobService.cs
├── Models/
│   ├── User.cs
│   ├── Customer.cs
│   ├── Product.cs
│   ├── Order.cs
│   └── ViewModels/
├── Views/
│   ├── Account/
│   ├── Admin/
│   ├── Order/
│   └── Shared/
└── wwwroot/

ST10439147_CLDV6212_POE.Functions/
├── Functions/
│   ├── CustomerFunction.cs
│   ├── FileUploadFunction.cs
│   ├── ProductFunction.cs
│   └── OrderFunction.cs
└── host.json
```

## Database Schema

### Users Table (SQL Database)
```sql
CREATE TABLE Users (
    UserId INT PRIMARY KEY IDENTITY(1,1),
    Email NVARCHAR(255) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(500) NOT NULL,
    Role NVARCHAR(50) NOT NULL,
    CustomerId NVARCHAR(255) NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedDate DATETIME NOT NULL,
    LastLoginDate DATETIME NULL
);
```

## Future Enhancements
- [ ] Implement password reset functionality
- [ ] Add email notifications for order status changes
- [ ] Implement shopping cart functionality
- [ ] Add product image uploads to Blob Storage
- [ ] Create reporting dashboard with charts
- [ ] Add export functionality (CSV, PDF)
- [ ] Implement real-time notifications using SignalR
- [ ] Add advanced search and filtering
- [ ] Implement inventory management
- [ ] Add customer reviews and ratings

## License
This project is for educational purposes as part of CLDV6212 coursework at IIE.

## Links

### Demonstration Videos
- **Part 3:** [Watch on YouTube](https://youtu.be/P-oUV2yOKHA)

### Live Deployment
- **Azure Functions App:** [View Live](https://st10439147-dbb4a3cbbedxcqd8.southafricanorth-01.azurewebsites.net)

## References & Resources

### AI Assistants
- **Claude AI:** 
  - [Session 1](https://claude.ai/chat/5b0f17e7-5de2-41e9-83ea-2b10483de6ec)
  - [Session 2](https://claude.ai/chat/d976e455-64cc-41f4-a240-0cfdb3969ce9)
- **ChatGPT:**
  - [Session 1](https://chatgpt.com/c/6916e686-ad9c-832b-b033-6485544951b9)
  - [Session ](https://chatgpt.com/c/6914ab8f-afa4-8332-b866-707e78f4ab88)

### Documentation & Tutorials
- **Microsoft Docs:** [ASP.NET Core Documentation](https://docs.microsoft.com/aspnet/core)
- **Azure Documentation:** [Azure Services Overview](https://docs.microsoft.com/azure)
- **W3Schools:** [Web Development Reference](https://www.w3schools.com/)

### Educational Resources
- **IIE VC School of Computer Science:** [YouTube Channel](https://www.youtube.com/@VCSOCS)
  - Azure services setup and configuration tutorials
  - Cloud development best practices
- **In-Class AzureApp Project:** Foundational concepts and implementation patterns

### Learning Materials
- ADO.NET database access patterns
- Azure Functions development and deployment
- ASP.NET Core MVC architecture
- Azure Storage services integration
- Authentication and authorization in ASP.NET Core

---

**Note:** This application is part of an academic project and may require additional hardening and optimization before production deployment. Always follow security best practices and conduct thorough testing before deploying to production environments.
