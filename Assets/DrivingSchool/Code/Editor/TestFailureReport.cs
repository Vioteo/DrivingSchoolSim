using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace DrivingSchool.Editor
{
    /// <summary>
    /// Writes every failed EditMode test (name, message, first stack line) of a run to artifacts/reports/editmode-failures.txt
    /// — the MCP runner returns only the last few results of a long run. The callback is registered again after every
    /// domain reload (scene tests reload it), and the counts live in SessionState, so a whole run is recorded.
    /// </summary>
    [InitializeOnLoad]
    public static class TestFailureReport
    {
        public const string ReportPath = "artifacts/reports/editmode-failures.txt";
        const string Armed = "DS.TestFailureReport.armed", Total = "DS.TestFailureReport.total", Failed = "DS.TestFailureReport.failed";

        static TestFailureReport() => ScriptableObject.CreateInstance<TestRunnerApi>().RegisterCallbacks(new Collector());

        [MenuItem("Driving School/Tests/Run EditMode → failure report")]
        public static void Run()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, "running\n");
            SessionState.SetBool(Armed, true); SessionState.SetInt(Total, 0); SessionState.SetInt(Failed, 0);
            ScriptableObject.CreateInstance<TestRunnerApi>().Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
        }

        sealed class Collector : ICallbacks
        {
            public void RunStarted(ITestAdaptor t) { }
            public void TestStarted(ITestAdaptor t) { }
            public void TestFinished(ITestResultAdaptor r)
            {
                if (!SessionState.GetBool(Armed, false) || r.HasChildren) return;
                SessionState.SetInt(Total, SessionState.GetInt(Total, 0) + 1);
                if (r.TestStatus != TestStatus.Failed) return;
                SessionState.SetInt(Failed, SessionState.GetInt(Failed, 0) + 1);
                var line = (r.StackTrace ?? "").Split('\n').FirstOrDefault(l => l.Contains(".cs:"))?.Trim();
                File.AppendAllText(ReportPath, r.FullName + "\n    " + (r.Message ?? "").Trim().Replace("\n", "\n    ") + "\n    @ " + line + "\n");
            }
            public void RunFinished(ITestResultAdaptor r)
            {
                if (!SessionState.GetBool(Armed, false)) return;
                SessionState.SetBool(Armed, false);
                File.AppendAllText(ReportPath, $"done: {SessionState.GetInt(Total, 0)} tests, {SessionState.GetInt(Failed, 0)} failed\n");
            }
        }
    }
}
