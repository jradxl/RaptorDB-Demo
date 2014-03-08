
using System;
namespace RaptorDBTest1
{
    class Program
    {
        static void Main(string[] args)
        {
            //Let's avoid statics everywhere!
            new RaptorDBTest1.Shell.Shell();

            Console.WriteLine("Press any key to quit.");
            Console.ReadKey();
        }
    }
}

