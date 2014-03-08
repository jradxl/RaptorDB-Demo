
namespace RaptorDBTest1.LoadSeedDocuments
{
    using RaptorDB;
    using RaptorDB.Common;
    using RaptorDBTest1Models;
    using System;
    using System.Collections.Generic;

    public class LoadSeedDocuments
    {
        private static Object _lock = new Object();

        public Boolean InsertSalesInvoices(int number, RaptorDBClient client)
        {
            Boolean rtn = false;
            SalesInvoice invoice = null;

            lock (_lock)
            {
                DateTime dt = FastDateTime.Now;
                int count = number;

                Random r = new Random();
                for (int i = 0; i < count; i++)
                {
                    invoice = new SalesInvoice()
                    {
                        Date = FastDateTime.Now.AddMinutes(r.Next(60)),
                        Serial = i % 10000,
                        CustomerName = "Customer Name " + i % 10,
                        NoCase = "Case " + i % 10,
                        Status = (byte)(i % 4),
                        Address = "Customer Address " + i % 10,
                        Approved = i % 100 == 0 ? true : false
                    };

                    invoice.Items = new List<LineItem>();

                    for (int k = 0; k < 5; k++)
                        invoice.Items.Add(new LineItem() { Product = "Product " + k, Discount = 0, Price = 10 + k, QTY = 1 + k });

                    rtn = client.Save(invoice.ID, invoice);
                }
                Console.WriteLine("Insert done in {0} seconds", FastDateTime.Now.Subtract(dt).TotalSeconds);
            }
            return rtn;
        }
    }
}
