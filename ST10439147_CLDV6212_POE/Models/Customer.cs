namespace ST10439147_CLDV6212_POE.Models
{
    public class Customer
    {
        // Azure Tables require PartitionKey and RowKey
        public string PartitionKey { get; set; }   // e.g., "Customer"
        public string RowKey { get; set; }         // Unique ID (e.g., CustomerId)

        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public string Address { get; set; }
    }
}
