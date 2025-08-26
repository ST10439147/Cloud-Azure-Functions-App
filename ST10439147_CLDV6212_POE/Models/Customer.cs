using Azure;
using Azure.Data.Tables;
using System;
using System.ComponentModel.DataAnnotations;

namespace ST10439147_CLDV6212_POE.Models
{
    public class Customer : ITableEntity
    {
        public Customer()
        {
            // Initialize RowKey with a new GUID if not already set
            RowKey = Guid.NewGuid().ToString();
            PartitionKey = "Customer";
        }

        public string PartitionKey { get; set; } = "Customer";
        public string RowKey { get; set; } // Unique CustomerId (GUID)

        [Required(ErrorMessage = "First name is required")]
        [StringLength(50, ErrorMessage = "First name cannot exceed 50 characters")]
        public string FirstName { get; set; }

        [Required(ErrorMessage = "Last name is required")]
        [StringLength(50, ErrorMessage = "Last name cannot exceed 50 characters")]
        public string LastName { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Phone number is required")]
        [Phone(ErrorMessage = "Invalid phone number format")]
        [StringLength(15, ErrorMessage = "Phone number cannot exceed 15 characters")]
        public string PhoneNumber { get; set; }

        // Audit fields
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//