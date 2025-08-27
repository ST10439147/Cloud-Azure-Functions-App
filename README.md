# ST10439147_CLDV6212_POE

## Overview
This is a Razor Pages web application for managing orders, customers, and products. It includes features for viewing, editing, and deleting orders, as well as queue message management and inventory updates for ABC Retail.
With the use of Azure's storage account features: Tables service, Queue Service, Blob Storage, File Share services.

## Features
- Order management (create, edit, delete)
- Customer and product details
- Queue message viewing and status
- Inventory update tracking
- Responsive UI with Bootstrap and FontAwesome

## Technologies
- ASP.NET Core Razor Pages (.NET 8, C# 12)
- Bootstrap 5
- FontAwesome
- Entity Framework Core
- Azure Queue Storage

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

## Link to YouTube video
  https://youtu.be/0ZcDPza-Glg
  
## Link to WebApp
  https://st10439147-dbb4a3cbbedxcqd8.southafricanorth-01.azurewebsites.net

## References
- ClaudAI - https://claude.ai/
- ChatGPT - https://chat.openai.com/
- W3schools - https://www.w3schools.com/
- IIEVC School of Computer Science Youtube channel for Azure services setup and use https://www.youtube.com/@VCSOCS
- AzureApp project done in class with lecturer
