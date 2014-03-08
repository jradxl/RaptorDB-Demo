
namespace RaptorDBTest1.Shell
{
    using RaptorDB;
    using RaptorDBTest1.LoadSeedDocuments;
    using RaptorDBTest1Views;
    using System;

    public class Shell
    {
        private RaptorDBClient _raptorDBClient;
        private RaptorDBServer _raptorDBServer;

        public Shell()
        {
            Console.WriteLine("RaptorDB Test One\n");
            try
            {
                //This Solution contains slightly modified RaptorDB sources.

                //For testing Client/Server mode, it's easy to start the server here.
                //It would be good if the server returned indication of a successful start.
                //The directories will be automatically created.
                //RaptorDB is self-initialising.
                //Server Log will be in C:\RaptorDB\DB\Logs
                _raptorDBServer = new RaptorDBServer(90, @"C:\RaptorDB\DB");


                //Change to false to avoid repeating these tests.
#if true
                //New feature. Test for wrong/non-listening port
                _raptorDBClient = new RaptorDBClient("localhost", 91, "admin", "admin");

                var err1 = _raptorDBClient.LastErrorMessage;
                if (!String.IsNullOrEmpty(err1))
                {
                    Console.WriteLine("Expected Error: {0}\n", err1);
                   // return;
                }

                //New feature. Test for non-authentication
                _raptorDBClient = new RaptorDBClient("localhost", 90, "admin", "adminxx");

                var err2 = _raptorDBClient.LastErrorMessage;
                if (!String.IsNullOrEmpty(err2))
                {
                    Console.WriteLine("Expected Error: {0}\n", err2);
                    //return;
                }
#endif

                //New feature. Now Authenticate Correctly. Authentication has been added to the constructor.
                _raptorDBClient = new RaptorDBClient("localhost", 90, "admin", "admin");
                var err3 = _raptorDBClient.LastErrorMessage;
                if (!String.IsNullOrEmpty(err3))
                {
                    Console.WriteLine("UnExpected Error: {0}\n", err3);
                    return;
                }
                else
                    Console.WriteLine("Let's assume we have authenticated correctly.\n");


                //Add a User. 
                Random r = new Random();
                var user = "john_" + r.Next(10000).ToString();
                var rtn1 = _raptorDBClient.AddUser(user, "", user);
                if (!rtn1)
                    Console.WriteLine("User {0} already exists or could not be created.\n", user);
                else
                    Console.WriteLine("User {0} created successfully.\n", user);
                Console.WriteLine();


                //New feature. List all Users
                var users = _raptorDBClient.GetUsers();
                if (users != null)
                {
                    Console.WriteLine("The following Users are defined:-\n");
                    foreach (var user2 in users)
                    {
                        Console.WriteLine("\t{0}", user2);
                    }
                }
                else
                {
                    Console.WriteLine("Gettings Users failed with error: {0}\n", _raptorDBClient.LastErrorMessage);
                }
                Console.WriteLine();


                //Add 100 Sales Invoices each time the program runs.
                var lsd = new LoadSeedDocuments();
                var rtn2 = lsd.InsertSalesInvoices(100, _raptorDBClient);

                if (rtn2)
                    Console.WriteLine("Documents inserted correctly.\n");
                else
                    Console.WriteLine("Document insert failed.\n");


                //New feature. Show the current views defined
                var views = _raptorDBClient.GetViews();
                if (views != null)
                {
                    Console.WriteLine("The following Views are defined:-\n");
                    foreach (var view in views)
                    {
                        Console.WriteLine("\t{0} with Count = {1}", view, _raptorDBClient.Count(view as String));
                    }
                }
                else
                {
                    Console.WriteLine("Gettings Views failed with error: {0}\n", _raptorDBClient.LastErrorMessage);
                }
                Console.WriteLine();


                Console.WriteLine("A string Query returning all rows");
                var results1 = _raptorDBClient.Query(typeof(SalesInvoiceView).Name);
                if (results1 != null)
                {
                    Console.WriteLine("Query returned {0} rows. Showing 10 ...\n", results1.Count);
                    for (int c1 = 0; c1 < 10; c1++)
                    {
                        var row = results1.Rows[c1] as SalesInvoiceView.RowSchema;
                        if (row != null)
                        {
                            Console.WriteLine("{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}", row.docid.ToString(), row.CustomerName, row.Address, row.Date, row.NoCase, row.Serial, row.Status, row.Approved);
                        }
                    }
                }
                else 
                { 
                    Console.WriteLine("Query failed with error: {0}\n", _raptorDBClient.LastErrorMessage);
                }
                Console.WriteLine();


                Console.WriteLine("A string Query returning filtered rows");
                //Multiple will be returned depending on how many times this program has been run.
                var results2 = _raptorDBClient.Query(typeof(SalesInvoiceView).Name, "CustomerName=\"Customer Name 9\"");
                if (results2 != null)
                {
                    Console.WriteLine("Query returned {0} rows. Showing 10 ...\n", results2.Count);
                    if (results2.Count > 0)
                    {
                        for (int c1 = 0; c1 < 10; c1++)
                        {
                            var row = results2.Rows[c1] as SalesInvoiceView.RowSchema;
                            if (row != null)
                            {
                                Console.WriteLine("{0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}", row.docid.ToString(), row.CustomerName, row.Address, row.Date, row.NoCase, row.Serial, row.Status, row.Approved);
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine("Query failed with error: {0}\n", _raptorDBClient.LastErrorMessage);
                }
                Console.WriteLine();


                Console.WriteLine("A typed and filtered Query");
                var results3 = _raptorDBClient.Query<NewSalesInvoiceView.RowSchema>(x => x.Product == "Product 4" );
                if (results3 != null)
                {
                    Console.WriteLine("Query returned {0} rows.\n", results3.Count);
                }
                else
                {
                    Console.WriteLine("Query failed with error: {0}\n", _raptorDBClient.LastErrorMessage);
                }
                Console.WriteLine();

            }
            catch (Exception ex)
            {
                Console.WriteLine("Untrapped Exception: {0}\n", ex.Message);
            }
            finally
            {
                _raptorDBServer.Shutdown();
                _raptorDBClient.Shutdown();
            }
        }
    }
}
