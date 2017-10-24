using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace HTTPProxyServer
{
    public class ProxyServer
    {
    
        private readonly int BUFFER_SIZE = 8192;
        private TcpListener _listener;
        private Thread _listenerThread;


        public ProxyServer()
        {
           
        }

        /// <summary>
        /// Setting Proxy port number while starting
        /// </summary>
        /// <param name="port">port number</param>
        public ProxyServer(string   port)
        {
            if (string.IsNullOrEmpty(port))
            {
                _listener = new TcpListener(IPAddress.Any, 8081);
            }
            else
            {
                _listener = new TcpListener(IPAddress.Any, Int32.Parse(port)); 
            }
        }

        /// <summary>
        /// Start the proxy server
        /// </summary>
        /// <returns></returns>
        public bool Start()
        {
            try
            {
                _listener.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return false;
            }

            _listenerThread = new Thread(new ParameterizedThreadStart(Listen));

            _listenerThread.Start(_listener);

            return true;
        }

        /// <summary>
        /// Start the proxy server
        /// </summary>
        public void Stop()
        {
            _listener.Stop();

            //wait till all the connections are processed

            _listenerThread.Abort();
            _listenerThread.Join();
            _listenerThread.Join();
        }

        /// <summary>
        /// TCP Listener
        /// </summary>
        /// <param name="obj"></param>
        private void Listen(Object obj)
        {
            TcpListener listener = (TcpListener)obj;
            try
            {
                while (true)
                {
                    TcpClient client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(new WaitCallback(this.ProcessClient), client);

                }
            }
            catch (Exception ex) { };
        }

        /// <summary>
        /// Process Client req
        /// </summary>
        /// <param name="obj">client request object</param>
        private void ProcessClient(Object obj)
        {
            TcpClient client = (TcpClient)obj;
            try
            {
                DoHttpProcessing(client);
            }
            catch (Exception ex)
            { }

            finally
            {
                client.Close();
            }
        }

        /// <summary>
        /// Method takes care of the individual http request i.e. processes the request
        /// </summary>
        /// <param name="client">this is the tcp client object of request</param>
        private void DoHttpProcessing(TcpClient client)
        {
            Stream clientStream = client.GetStream();
            Stream outStream = clientStream; 
       
            StreamReader clientStreamReader = new StreamReader(clientStream);    
            
            try
            {
                //read the first line HTTP command
                String httpCmd = clientStreamReader.ReadLine();
                if (String.IsNullOrEmpty(httpCmd))
                {
                    clientStreamReader.Close();
                    clientStream.Close();
                    return;
                }

                //break up the line into three components
                String[] splitBuffer = httpCmd.Split(new char[] { ' ' }, 3);

                String method = splitBuffer[0];
                String remoteUri = splitBuffer[1];
                Version version = new Version(1, 0);

                HttpWebRequest webReq;
                HttpWebResponse response = null;
               

                //construct the web request that we are going to issue on behalf of the client.
                webReq = (HttpWebRequest)HttpWebRequest.Create(remoteUri);
                webReq.Method = method;
                webReq.ProtocolVersion = version;

                //read the request headers from the client and copy them to our request
                int contentLen = ReadRequestHeaders(clientStreamReader, webReq);
                
                webReq.Proxy = null;
                webReq.KeepAlive = false;
                webReq.AllowAutoRedirect = false;
                webReq.AutomaticDecompression = DecompressionMethods.None;


                //Write the header to console
                    Console.WriteLine(String.Format("{0} {1} HTTP/{2}",webReq.Method,webReq.RequestUri.AbsoluteUri, webReq.ProtocolVersion));
                    ShowHeader(webReq.Headers);            

                    webReq.Timeout = 15000;

                    try
                    {
                        response = (HttpWebResponse)webReq.GetResponse();
                    }
                    catch (WebException webEx)
                    {
                        response = webEx.Response as HttpWebResponse;
                    }
                    if (response != null)
                    {
                        List<Tuple<String,String>> responseHeaders = ProcessResponse(response);
                        StreamWriter myResponseWriter = new StreamWriter(outStream);
                        Stream responseStream = response.GetResponseStream();
                        try
                        {
                            //send the response status and response headers
                            myResponseWriter.WriteLine(String.Format("HTTP/1.0 {0} {1}", (Int32)response.StatusCode, response.StatusDescription));

                            if (responseHeaders != null)
                            {
                                foreach (Tuple<String, String> header in responseHeaders)
                                    myResponseWriter.WriteLine(String.Format("{0}: {1}", header.Item1, header.Item2));
                            }
                            myResponseWriter.WriteLine();
                            myResponseWriter.Flush();

                            Byte[] buffer;
                            if (response.ContentLength > 0)
                                buffer = new Byte[response.ContentLength];
                            else
                                buffer = new Byte[BUFFER_SIZE];

                            int bytesRead;

                            while ((bytesRead = responseStream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                
                                outStream.Write(buffer, 0, bytesRead);
                          
                            }
                            
                            responseStream.Close();
                            

                            outStream.Flush();
                          
                        }
                        catch (Exception ex)
                        {}
                        finally
                        {
                            responseStream.Close();
                            response.Close();
                            myResponseWriter.Close();
                        }
                    }
              
            }
            catch (Exception ex)
            {}
            finally
            {
                clientStreamReader.Close();
                clientStream.Close();
                outStream.Close();              
            }

        }

        /// <summary>
        /// Method to process the response header
        /// </summary>
        /// <param name="response"></param>
        /// <returns></returns>
        private List<Tuple<String,String>> ProcessResponse(HttpWebResponse response)
        {
        
            List<Tuple<String, String>> returnHeaders = new List<Tuple<String, String>>();
            foreach (String s in response.Headers.Keys)
            {
                    returnHeaders.Add(new Tuple<String, String>(s, response.Headers[s]));
            }
            return returnHeaders;
        }

     
        /// <summary>
        /// Method to show header
        /// </summary>
        /// <param name="headers"></param>
        private void ShowHeader(WebHeaderCollection headers)
        {
            foreach (String s in headers.AllKeys)
                Console.WriteLine(String.Format("{0}: {1}", s,headers[s]));
            Console.WriteLine();
        }

        /// <summary>
        /// Method to extract the header from the request
        /// </summary>
        /// <param name="sr"></param>
        /// <param name="webReq"></param>
        /// <returns></returns>
        private int ReadRequestHeaders(StreamReader sr, HttpWebRequest webReq)
        {
            String httpCmd;
            int contentLen = 0;
            do
            {
                httpCmd = sr.ReadLine();
                if (String.IsNullOrEmpty(httpCmd))
                    return contentLen;
                String[] header = httpCmd.Split(new string[] { ": " }, 2, StringSplitOptions.None);
                switch (header[0].ToLower())
                {
                    case "host":
                        webReq.Host = header[1];
                        break;
                    case "user-agent":
                        webReq.UserAgent = header[1];
                        break;
                    case "accept":
                        webReq.Accept = header[1];
                        break; 
                    case "referer":
                        webReq.Referer = header[1];
                        break;
                    case "cookie":
                        webReq.Headers["Cookie"] = header[1];
                        break;
                    case "proxy-connection":
                    case "connection":
                    case "keep-alive":
                        //ignoring these
                        break;
                    case "content-length":
                        int.TryParse(header[1], out contentLen);
                        break;
                    case "content-type":
                        webReq.ContentType = header[1];
                        break;
                    case "if-modified-since":
                        String[] sb = header[1].Trim().Split(new char[] { ';' });
                        DateTime d;
                        if (DateTime.TryParse(sb[0], out d))
                            webReq.IfModifiedSince = d;
                        break;
                    default:
                        try
                        {
                            webReq.Headers.Add(header[0], header[1]);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(String.Format("Could not add header {0}.  Exception message:{1}", header[0], ex.Message));
                        }
                        break;
                }
            } while (!String.IsNullOrWhiteSpace(httpCmd));
            return contentLen;
        }
    }
}
