
namespace RaptorDBTest1Models
{
    using System;
    using System.Collections.Generic;

    public enum State
    {
        Open,
        Closed,
        Approved
    }

    public class LineItem
    {
        public decimal QTY { get; set; }
        public string Product { get; set; }
        public decimal Price { get; set; }
        public decimal Discount { get; set; }
    }

    public class SalesInvoice
    {
        public SalesInvoice()
        {
            ID = Guid.NewGuid();
        }

        public Guid ID { get; set; }
        public string CustomerName { get; set; }
        public string NoCase { get; set; }
        public string Address { get; set; }
        public List<LineItem> Items { get; set; }
        public DateTime Date { get; set; }
        public int Serial { get; set; }
        public byte Status { get; set; }
        public bool Approved { get; set; }
        public State InvoiceState { get; set; }
    }
}
