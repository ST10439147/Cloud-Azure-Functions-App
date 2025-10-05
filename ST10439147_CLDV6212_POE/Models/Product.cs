using Azure;
using Azure.Data.Tables;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ST10439147_CLDV6212_POE.Models
{
    public class Product : ITableEntity
    {
        public string PartitionKey { get; set; } = "Product";
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
        public double Price { get; set; }

        [Required(ErrorMessage = "Stock quantity is required")]
        [Range(0, int.MaxValue, ErrorMessage = "Stock quantity cannot be negative")]
        [Display(Name = "Stock Quantity")]
        public int StockQuantity { get; set; }

        [Url(ErrorMessage = "Invalid URL format")]
        [Display(Name = "Image")]
        public string? ImageUrl { get; set; }

        [JsonIgnore] // Ignore during JSON serialization
        public DateTimeOffset? Timestamp { get; set; }

        [JsonIgnore] // Ignore during JSON serialization - THIS IS THE KEY FIX
        public ETag ETag { get; set; }
    }
}