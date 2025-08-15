namespace ST10439147_CLDV6212_POE.Models
{
    public class Product
    {
        public string PartitionKey { get; set; }   // e.g., "Product"
        public string RowKey { get; set; }         // ProductId

        public string Name { get; set; }
        public string Description { get; set; }
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }

        // URL of the image stored in Azure Blob Storage
        public string ImageUrl { get; set; }
    }
}
