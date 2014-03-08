
namespace RaptorDBTest1.RaptorDBViews
{
    using RaptorDB;
    using RaptorDBTest1.DataModels;
    using System;

    #region [  views  ]

    public class SalesInvoiceViewRowSchema : RDBSchema
    {
        //[FullText]
        public string CustomerName;
        [CaseInsensitive]
        public string NoCase;
        public DateTime Date;
        public string Address;
        public int Serial;
        public byte Status;
        public bool Approved;
        public State InvoiceState;
    }

    [RegisterView]
    public class SalesInvoiceView : View<SalesInvoice>
    {
        public SalesInvoiceView()
        {
            this.Name = "SalesInvoice";
            this.Description = "A primary view for SalesInvoices";
            this.isPrimaryList = true;
            this.isActive = true;
            this.BackgroundIndexing = true;
            this.Version = 3;
            //// uncomment the following for transaction mode
            //this.TransactionMode = true;

            this.Schema = typeof(SalesInvoiceViewRowSchema);

            this.FullTextColumns.Add("customername"); // this or the attribute

            this.CaseInsensitiveColumns.Add("nocase"); // this or the attribute

            this.AddFireOnTypes(typeof(SalesInvoice));

            this.Mapper = (api, docid, doc) =>
            {
                //int c = api.Count("SalesItemRows", "product = \"prod 1\"");
                if (doc.Serial == 0)
                    api.RollBack();
                api.EmitObject(docid, doc);
            };
        }
    }

    public class SalesItemRowsViewRowSchema : RDBSchema
    {
        public string Product;
        public decimal QTY;
        public decimal Price;
        public decimal Discount;
    }

    [RegisterView]
    public class SalesItemRowsView : View<SalesInvoice>
    {
        public SalesItemRowsView()
        {
            this.Name = "SalesItemRows";
            this.Description = "";
            this.isPrimaryList = false;
            this.isActive = true;
            this.BackgroundIndexing = true;

            this.Schema = typeof(SalesItemRowsViewRowSchema);

            this.AddFireOnTypes(typeof(SalesInvoice));

            this.Mapper = (api, docid, doc) =>
            {
                if (doc.Status == 3 && doc.Items != null)
                    foreach (var item in doc.Items)
                        api.EmitObject(docid, item);
            };
        }
    }

    public class NewViewRowSchema : RDBSchema
    {
        public string Product;
        public decimal QTY;
        public decimal Price;
        public decimal Discount;
    }

    [RegisterView]
    public class NewView : View<SalesInvoice>
    {
        public NewView()
        {
            this.Name = "NewView";
            this.Description = "";
            this.isPrimaryList = false;
            this.isActive = true;
            this.BackgroundIndexing = true;
            this.Version = 1;

            this.Schema = typeof(NewViewRowSchema);

            this.AddFireOnTypes(typeof(SalesInvoice));

            this.Mapper = (api, docid, doc) =>
            {
                if (doc.Status == 3 && doc.Items != null)
                    foreach (var i in doc.Items)
                        api.EmitObject(docid, i);
            };
        }
    }
    #endregion
}
