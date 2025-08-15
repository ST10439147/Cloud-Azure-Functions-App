using Azure;
using Azure.Data.Tables;
using System;

namespace ST10439147_CLDV6212_POE.Models
{
    public class Customer : ITableEntity
    {
        public string PartitionKey { get; set; } = "Customer";
        public string RowKey { get; set; } // Unique CustomerId (GUID)
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }

        // Audit fields
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
