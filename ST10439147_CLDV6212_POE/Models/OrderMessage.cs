using System;

namespace ST10439147_CLDV6212_POE.Models
{
    public class OrderMessage
    {
        public string OrderId { get; set; }      // Matches Order.RowKey
        public string CustomerId { get; set; }   // Matches Customer.RowKey
        public string ProductId { get; set; }    // Matches Product.RowKey
        public int Quantity { get; set; }
        public double TotalPrice { get; set; } // Changed to double for consistency with Order model
        public DateTime OrderDate { get; set; }

        // Status or action type (e.g. "NewOrder", "UpdateOrder")
        public string Action { get; set; } = "NewOrder";
    }
}
//-----------------------------------------------------DDDDooooo END OF FILE oooooDDDD-----------------------------------------------------//
