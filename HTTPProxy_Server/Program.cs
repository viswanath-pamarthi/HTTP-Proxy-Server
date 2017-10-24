using System;

namespace HTTPProxyServer
{
    class Program
    {
 
        static void Main(string[] args)
        {
            string portNumber = null;
            if (args.Length > 0)
            {
               portNumber = string.IsNullOrEmpty(args[0]) ? null : args[0];
            }
            ProxyServer proxy = new ProxyServer(portNumber);
            if (proxy.Start())
            {
                Console.WriteLine("Server started and Listening to requests:\n");
                Console.ReadLine();
                Console.WriteLine("Closing Proxy Server...");
                proxy.Stop();
                Console.WriteLine("Proxy Server stopped...");
            }
            Console.WriteLine("Press enter to exit");
            Console.ReadLine();
        }
    }



}
