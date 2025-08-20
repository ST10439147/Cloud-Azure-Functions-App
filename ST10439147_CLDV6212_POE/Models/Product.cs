using Azure;
using Azure.Data.Tables;
using System.ComponentModel.DataAnnotations;

namespace ST10439147_CLDV6212_POE.Models
{
    public class Product : ITableEntity
    {
        public string PartitionKey { get; set; } = "Product";

        // RowKey should not be required for user input - it's auto-generated
        public string RowKey { get; set; } = string.Empty;

        [Required(ErrorMessage = "Product name is required")]
        [StringLength(100, ErrorMessage = "Product name cannot exceed 100 characters")]
        [Display(Name = "Product Name")]
        public string Name { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
        [Display(Name = "Description")]
        public string Description { get; set; } = string.Empty;

        [Required(ErrorMessage = "Price is required")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Price must be greater than 0")]
        [Display(Name = "Price")]
        [DataType(DataType.Currency)]
        public decimal Price { get; set; }

        [Required(ErrorMessage = "Stock quantity is required")]
        [Range(0, int.MaxValue, ErrorMessage = "Stock quantity cannot be negative")]
        [Display(Name = "Stock Quantity")]
        public int StockQuantity { get; set; }

        // ImageUrl is optional and auto-generated - removed Required attribute
        // Keep it nullable to handle cases where no image is provided
        [Url(ErrorMessage = "Invalid URL format")]
        [Display(Name = "Image")]
        public string? ImageUrl { get; set; }

        // Audit fields - these should not be validated by user input
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}