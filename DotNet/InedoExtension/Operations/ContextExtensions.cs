using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Extensibility.Operations;

namespace Inedo.Extensions.DotNet.Operations
{
    internal static class ContextExtensions
    {
        private static readonly LazyRegex DurationRegex = new(@"^(?<1>[0-9]+):(?<2>[0-9]+):(?<3>[0-9]+)(\.(?<4>[0-9]+))?$", RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        /// <summary>
        /// Reads a .trx unit test result file and records its results in BuildMaster.
        /// </summary>
        /// <param name="context">The operation context.</param>
        /// <param name="trxFileName">Full path to the .trx file to process. This must be an absolute path and the file must exist.</param>
        /// <param name="testGroup">Group to record tests under in BuildMaster. May be null to use the default group.</param>
        public static async Task RecordUnitTestResultsAsync(this IOperationExecutionContext context, string trxFileName, string testGroup)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrEmpty(trxFileName))
                throw new ArgumentNullException(nameof(trxFileName));

            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();

            XDocument doc;
            using (var file = await fileOps.OpenFileAsync(trxFileName, FileMode.Open, FileAccess.Read))
            using (var reader = new XmlTextReader(file) { Namespaces = false })
            {
                if (file.CanSeek) file.Position = 0;
                doc = XDocument.Load(reader);
            }

            var testRecorder = await context.TryGetServiceAsync<IUnitTestRecorder>();
            bool failures = false;

            foreach (var result in doc.Element("TestRun").Element("Results").Elements("UnitTestResult"))
            {
                var testName = (string)result.Attribute("testName");
                var outcome = (string)result.Attribute("outcome");
                var output = result.Element("Output");
                UnitTestStatus status;
                string testResult;

                if (string.Equals(outcome, "Passed", StringComparison.OrdinalIgnoreCase))
                {
                    status = UnitTestStatus.Passed;
                    testResult = "Passed";
                }
                else if (string.Equals(outcome, "NotExecuted", StringComparison.OrdinalIgnoreCase))
                {
                    status = UnitTestStatus.Inconclusive;
                    if (output == null)
                        testResult = "Ignored";
                    else
                        testResult = GetResultTextFromOutput(output);
                }
                else
                {
                    status = UnitTestStatus.Failed;
                    testResult = GetResultTextFromOutput(output);
                    failures = true;
                }

                if (testRecorder != null)
                {
                    var startDate = (DateTimeOffset)result.Attribute("startTime");
                    var duration = ParseDuration((string)result.Attribute("duration"));
                    await testRecorder.RecordUnitTestAsync(AH.CoalesceString(testGroup, "Unit Tests"), testName, status, testResult, startDate, duration);
                }
            }

            if (failures)
                context.Log.LogError("One or more unit tests failed.");
            else
                context.Log.LogInformation("Tests completed with no failures.");
        }

        private static TimeSpan ParseDuration(string s)
        {
            if (!string.IsNullOrWhiteSpace(s))
            {
                var m = DurationRegex.Match(s);
                if (m.Success)
                {
                    int hours = int.Parse(m.Groups[1].Value);
                    int minutes = int.Parse(m.Groups[2].Value);
                    int seconds = int.Parse(m.Groups[3].Value);

                    var timeSpan = new TimeSpan(hours, minutes, seconds);

                    if (m.Groups[4].Success)
                    {
                        var fractionalSeconds = double.Parse("0." + m.Groups[4].Value, CultureInfo.InvariantCulture);
                        timeSpan += TimeSpan.FromSeconds(fractionalSeconds);
                    }

                    return timeSpan;
                }
            }

            return TimeSpan.Zero;
        }
        private static string GetResultTextFromOutput(XElement output)
        {
            var message = string.Empty;
            var errorInfo = output.Element("ErrorInfo");
            if (errorInfo != null)
            {
                message = (string)errorInfo.Element("Message");
                var trace = (string)errorInfo.Element("StackTrace");
                if (!string.IsNullOrEmpty(trace))
                    message += Environment.NewLine + trace;
            }

            return message;
        }
    }
}
