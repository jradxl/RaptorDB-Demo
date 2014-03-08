
namespace RaptorDBTest1Views
{
    using RaptorDB;
    using RaptorDBTest1Models;
    using System;

    /// <summary>
    /// Required to force server to re-load Views when changed.
    /// Remember to update when you make changes, or you're be 
    /// debugging for hours!
    /// </summary>
    public struct SchemaVersion
    {
        public static int schemaVersion = 33;
    }

    [RegisterView]
    public class SalesInvoiceView : View<SalesInvoice>
    {
        public class RowSchema : RDBSchema
        {
            //Not required as the DocID is included by default
            //public Guid ID;

            [FullText]
            public string CustomerName;

            [CaseInsensitive]
            public string NoCase;

            public string Address;

            public DateTime Date;
            public int Serial;
            public byte Status;
            public bool Approved;

            //Unfortunately, Enum does not work
            //public State InvoiceState;
        }

        public SalesInvoiceView()
        {
            this.Name = "SalesInvoiceView";
            this.Description = "A primary view for SalesInvoices";
            this.isPrimaryList = true;
            this.isActive = true;
            this.BackgroundIndexing = true;
            this.Version = SchemaVersion.schemaVersion;
            //this.ConsistentSaveToThisView = true;

            //Uncomment the following for transaction mode
            //this.TransactionMode = true;

            this.Schema = typeof(SalesInvoiceView.RowSchema);

            //this.FullTextColumns.Add("customername"); // this or the attribute

            //this.CaseInsensitiveColumns.Add("nocase"); // this or the attribute

            this.AddFireOnTypes(typeof(SalesInvoice));

            this.Mapper = (api, docid, doc) =>
            {
                //int c = api.Count("SalesItemRows", "product = \"prod 1\"");               
                //if (doc.Serial == 0) api.RollBack();
                api.EmitObject(docid, doc);
            };
        }
    }

    [RegisterView]
    public class SalesItemRowsView : View<SalesInvoice>
    {
        public class RowSchema : RDBSchema
        {
            public string Product;
            public decimal QTY;
            public decimal Price;
            public decimal Discount;
        }

        public SalesItemRowsView()
        {
            this.Name = "SalesItemRowsView";
            this.Description = "Sales Invoice Item Rows";
            this.isPrimaryList = false;
            this.isActive = true;
            this.BackgroundIndexing = true;
            this.Version = SchemaVersion.schemaVersion;

            this.Schema = typeof(SalesItemRowsView.RowSchema);

            this.AddFireOnTypes(typeof(SalesInvoice));

            this.Mapper = (api, docid, doc) =>
            {
                if (doc.Status == 3 && doc.Items != null)
                    foreach (var item in doc.Items)
                        api.EmitObject(docid, item);
            };
        }
    }

    [RegisterView]
    public class NewSalesInvoiceView : View<SalesInvoice>
    {
        public class RowSchema : RDBSchema
        {
            public string Product;
            public decimal QTY;
            public decimal Price;
            public decimal Discount;
        }

        public NewSalesInvoiceView()
        {
            this.Name = "NewSalesInvoiceView";
            this.Description = "";
            this.isPrimaryList = false;
            this.isActive = true;
            this.BackgroundIndexing = true;
            this.Version = SchemaVersion.schemaVersion;

            this.Schema = typeof(NewSalesInvoiceView.RowSchema);

            this.AddFireOnTypes(typeof(SalesInvoice));

            this.Mapper = (api, docid, doc) =>
            {
                if (doc.Status == 3 && doc.Items != null)
                    foreach (var i in doc.Items)
                        api.EmitObject(docid, i);
            };
        }
    }
}
