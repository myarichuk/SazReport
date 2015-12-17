using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fiddler;
using System.Reflection;
using System.IO;
using System.Net.Http;
using metrics;
using Org.BouncyCastle.Math;
using ServiceStack.Text;

namespace SazReport
{

    public class RequestResponseInfo
    {
        public int Id { get; set; }

        public string Url { get; set; }

        public int RequestLength { get; set; }

        public int ResponseLength { get; set; }

        public string[] RequestHeaders { get; set; } = new string[0];

        public string[] ResponseHeaders { get; set; } = new string[0];

        public string Method { get; set; }

        public TimeSpan RequestLatency { get; set; }

        public TimeSpan ServerLatency { get; set; }

        public double RequestLatencyMs => RequestLatency.TotalMilliseconds;

        public int ResponseCode { get; set; }

    }

    public class Stats<T> where T : struct
    {
        public T Min { get; set; }

        public T Max { get; set; }

        public T Average { get; set; }
        public double StdDev { get; set; }
    }

    public class SazReport
    {
        public Stats<TimeSpan> RequestLatencyStats { get; } = new Stats<TimeSpan>();

        public Stats<double> RequestSizeStats { get; } = new Stats<double>();

        public Dictionary<int, int> ResponseCodeStats { get; set; } = new Dictionary<int, int>();

        public List<RequestResponseInfo> LargestRequests { get; set; } = new List<RequestResponseInfo>();

        public List<RequestResponseInfo> LargestResponses { get; set; } = new List<RequestResponseInfo>();

        public List<RequestResponseInfo> LongestRequests { get; set; } = new List<RequestResponseInfo>();

        public Dictionary<string, int> UrlStats { get; set; } = new Dictionary<string, int>();

        public Dictionary<string, double> RequestSizesHistogram { get; set; } = new Dictionary<string, double>();

        public Dictionary<string, double> RequestLatenciesHistogram { get; set; } = new Dictionary<string, double>();
    }

    class Program
    {
        private const int TopItemCount = 5;

        static void Main(string[] args)
        {
            Console.WriteLine("Fiddler SAZ Reporting Tool");
            Console.WriteLine();
            if (args.Length != 1)
            {
                Console.WriteLine("Usage : SazReport.exe [full path to Fiddler SAZ session file]");
                return;
            }

            if (!File.Exists(args[0]))
            {
                Console.WriteLine("Couldn't find SAZ archive at {0}...", args[0]);
                return;
            }

            if (!FiddlerApplication.oTranscoders
                                   .ImportTranscoders(Assembly.GetExecutingAssembly().Location))
            {
                Console.WriteLine("This assembly was not compiled with a SAZ-exporter, cannot continue.");
                Console.WriteLine("Before all else, please verify that the project was compiled with SAZ_SUPPORT conditional symbol.");
                Console.WriteLine("This is a prerequisite for a fiddler library to support handling SAZ session archives");
                return;
            }

            var dictOptions = new Dictionary<string, object> { { "Filename", args[0] } };

            Console.Write("Loading SAZ file...");
            var sessions = FiddlerApplication.DoImport("SAZ", false, dictOptions, null);

            if (sessions == null)
            {
                Console.WriteLine("Loading failed. \n FiddlerApplication.DoImport() returned null...");
                return;
            }
            Console.WriteLine("done");

            Console.Write("Analyzing...");

            var report = new SazReport();
            var requestResponseInformation = (from session in sessions
                                              let clientLatency = session.Timers.ClientDoneResponse -
                                                                 session.Timers.ClientBeginRequest
                                              let serverLatency = session.Timers.ServerConnected -
                                                                  session.Timers.ServerDoneResponse
                                              select new RequestResponseInfo
                                              {
                                                  Id = session.id,
                                                  RequestHeaders = session.RequestHeaders.Select(x => $"{x.Name} = {x.Value}").ToArray(),
                                                  ResponseHeaders = session.ResponseHeaders.Select(x => $"{x.Name} = {x.Value}").ToArray(),
                                                  Method = session.RequestMethod,
                                                  Url = session.fullUrl,
                                                  RequestLatency = clientLatency,
                                                  ServerLatency = serverLatency,
                                                  RequestLength = session.RequestBody.Length,
                                                  ResponseLength = session.ResponseBody.Length,
                                                  ResponseCode = session.responseCode
                                              }).ToList();

            report.LongestRequests = requestResponseInformation.OrderByDescending(x => x.RequestLatency)
                                                               .Take(TopItemCount).ToList();

            report.LargestRequests = requestResponseInformation.OrderByDescending(x => x.RequestLength)
                                                               .Take(TopItemCount).ToList();

            report.LargestResponses = requestResponseInformation.OrderByDescending(x => x.ResponseLength)
                                                                .Take(TopItemCount).ToList();

            report.ResponseCodeStats = (from requestResponse in requestResponseInformation
                                        group requestResponse by requestResponse.ResponseCode into g
                                        select new
                                        {
                                            Code = g.Key,
                                            Count = g.Count()
                                        }).ToDictionary(x => x.Code, x => x.Count);

            report.UrlStats = (from requestResponse in requestResponseInformation
                               let uri = new Uri(requestResponse.Url).GetLeftPart(UriPartial.Path)
                               let urlString = uri.Substring(0, uri.LastIndexOf("/"))
                               group requestResponse by urlString into g
                               select new
                               {
                                   Url = g.Key,
                                   Count = g.Count()
                               }).ToDictionary(x => x.Url, x => x.Count);

            report.RequestLatencyStats.Max = requestResponseInformation.Max(x => x.RequestLatency);
            report.RequestLatencyStats.Min = requestResponseInformation.Min(x => x.RequestLatency);
            report.RequestLatencyStats.Average = TimeSpan.FromMilliseconds(requestResponseInformation.Average(x => x.RequestLatency.TotalMilliseconds));

            using (var metrics = new Metrics())
            {
                var requestSizeHistogram = metrics.Histogram("histograms", "Request Sizes (bytes)");
                var requestLatencyHistogram = metrics.Histogram("histograms", "Request Latency (milliseconds)");
                foreach (var requestResponse in requestResponseInformation)
                {
                    requestSizeHistogram.Update((long)requestResponse.RequestLength);
                    requestLatencyHistogram.Update((long)requestResponse.RequestLatency.TotalMilliseconds);
                }

                var percentages = requestSizeHistogram.Percentiles(0.25, 0.5, 0.9);

                report.RequestSizesHistogram.Add("25%", percentages[0]);
                report.RequestSizesHistogram.Add("50%", percentages[1]);
                report.RequestSizesHistogram.Add("90%", percentages[2]);

                report.RequestSizeStats.Max = requestSizeHistogram.Max;
                report.RequestSizeStats.Min = requestSizeHistogram.Min;
                report.RequestSizeStats.Average = requestSizeHistogram.Mean;
                report.RequestSizeStats.StdDev = requestSizeHistogram.StdDev;

                percentages = requestLatencyHistogram.Percentiles(0.1, 0.5, 0.9, 0.99);
                report.RequestLatenciesHistogram.Add("25%", percentages[0]);
                report.RequestLatenciesHistogram.Add("50%", percentages[1]);
                report.RequestLatenciesHistogram.Add("90%", percentages[2]);
            }

            Console.WriteLine("done");
            var sazFileInfo = new FileInfo(args[0]);

            Console.Write("Writing request information to csv...");
            var csvPath = $"{sazFileInfo.Directory?.FullName}{Path.DirectorySeparatorChar}{sazFileInfo.Name}.csv";

            using (var fs = new FileStream(csvPath, FileMode.Create))
            {
                foreach (var req in requestResponseInformation)
                    CsvSerializer.SerializeToStream(req, fs);
                fs.Flush();
            }

            Console.WriteLine($"done, {csvPath}");

            Console.Write("Writing SAZ report...");
            var jsonPath = $"{sazFileInfo.Directory?.FullName}{Path.DirectorySeparatorChar}{sazFileInfo.Name}.json";

            File.WriteAllText(jsonPath, report.Dump());
            Console.WriteLine($"done, {jsonPath}");
        }
    }
}