# ST10439147_CLDV6212_POE

## Overview
This is a Razor Pages web application for managing orders, customers, and products. It includes features for viewing, editing, and deleting orders, as well as queue message management and inventory updates for ABC Retail.
With the use of Azure's storage account features: Tables service, Queue Service, Blob Storage, File Share services.

## Features
- Order, Customer, Product management (create, edit, delete)
- Order, Customer and product details in the index view
- Queue message viewing and status
- Inventory update tracking
- Responsive UI with Bootstrap and FontAwesome

## Technologies
- ASP.NET Core Razor Pages (.NET 8, C# 12)
- Bootstrap 5
- FontAwesome
- Entity Framework Core
- Azure Queue Storage
- Azure Table Storage
- Azure Blob Storage
- Azure FileShare Storage

  ## NuGet Packages Used

This project uses the following NuGet packages:

- **Azure.Storage.Queues**  
  Provides client libraries for working with Azure Queue Storage, including sending, receiving, and managing queue messages.

- **Microsoft.Extensions.Configuration**  
  Enables configuration management, such as reading settings from appsettings.json.

- **Microsoft.Extensions.Logging**  
  Provides logging infrastructure for .NET applications.

- **System.Text.Json**  
  Used for high-performance JSON serialization and deserialization.

## Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Visual Studio 2022 or later

### Setup
1. Clone the repository:
2. Open the solution in Visual Studio.
3. Restore NuGet packages (__Restore NuGet Packages__).
4. Build the project (__Build Solution__).

### Running the Application
- Press __F5__ or click __Start Debugging__ in Visual Studio.
- The app will launch in your browser.

## Usage
- Navigate to `/Order` to manage orders.
- Use the queue message pages to view and manage messages.
- Edit or delete orders using the provided UI.

## Project Structure
- `Services/QueueService.cs`: Handles queue operations.
- `Controllers/OrderController.cs`: Manages order actions.
- `Views/Order/`: Razor views for order management.

## License
This project is for educational purposes.

## Link to YouTube video(Just incase the Azure Resources get dropped)
  Part 1: https://youtu.be/0ZcDPza-Glg
  
## Link to WebApp
  https://st10439147-dbb4a3cbbedxcqd8.southafricanorth-01.azurewebsites.net

## References
- ClaudAI - https://claude.ai/
- ChatGPT - https://chat.openai.com/
- W3schools - https://www.w3schools.com/
- IIEVC School of Computer Science Youtube channel for Azure services setup and use https://www.youtube.com/@VCSOCS
- AzureApp project done in class with lecturer
