
namespace RaptorDBTest1.ServerSideViews
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public class ServerSide
    {
        // so the result can be serialized and is not an anonymous type
        // since this uses fields, derive from the BindableFields for data binding to work
        public class SumType : RaptorDB.BindableFields
        {
            public string Product;
            public decimal TotalPrice;
            public decimal TotalQTY;
        }

        public static List<object> Sum_Products_based_on_filter(RaptorDB.Common.IRaptorDB rap, string filter)
        {
            var q = rap.Query<RaptorDBTest1Views.SalesItemRowsView.RowSchema>(filter);

            var res = from x in q.Rows
                      group x by x.Product into g
                      select new SumType //avoid annonymous types
                      {
                          Product = g.Key,
                          TotalPrice = g.Sum(p => p.Price),
                          TotalQTY = g.Sum(p => p.QTY)
                      };

            return res.ToList<object>();
        }
    }
}
