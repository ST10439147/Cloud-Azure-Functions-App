using Azure;
using Azure.Data.Tables;
using System.ComponentModel.DataAnnotations;

namespace ST10439147_CLDV6212_POE.Models
{
    public class Order : ITableEntity
    {
        public Order()
        {
            PartitionKey = "Order";
            RowKey = Guid.NewGuid().ToString();
            OrderDate = DateTime.UtcNow;
            Status = "Pending";
        }

        public string PartitionKey { get; set; } = "Order";
        public string RowKey { get; set; } = string.Empty; // Unique OrderId (GUID)

        [Required(ErrorMessage = "Customer is required")]
        [Display(Name = "Customer")]
        public string CustomerId { get; set; } = string.Empty; // Links to Customer.RowKey

        [Required(ErrorMessage = "Product is required")]
        [Display(Name = "Product")]
        public string ProductId { get; set; } = string.Empty;  // Links to Product.RowKey

        [Required(ErrorMessage = "Quantity is required")]
        [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1")]
        [Display(Name = "Quantity")]
        public int Quantity { get; set; } = 1;

        [Required(ErrorMessage = "Total price is required")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Total price must be greater than 0")]
        [Display(Name = "Total Price")]
        [DataType(DataType.Currency)]
        public double TotalPrice { get; set; } // decimal changed to double for compatibility

        [Display(Name = "Order Date")]
        public DateTime OrderDate { get; set; }

        [StringLength(50, ErrorMessage = "Status cannot exceed 50 characters")]
        [Display(Name = "Status")]
        public string Status { get; set; } = "Pending"; // e.g. Pending, Processing, Shipped, Completed, Cancelled

        // Audit fields
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//