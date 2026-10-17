// Console error watch: counts Error/Exception/Assert logs since last clear.
// Preflight uses this so captures never run against a broken pipeline.
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace NeonKaido.EditorTools
{
    [InitializeOnLoad]
    public static class ErrorWatch
    {
        public static int Count;          // since last Clear()
        public static string Last = "";   // last error message (truncated)
        public static int SinceDomain;    // never cleared

        static ErrorWatch()
        {
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            Count++; SinceDomain++;
            if (Last.Length < 200) Last = (Last.Length > 0 ? Last + " || " : "") + condition;
            if (Last.Length > 500) Last = Last.Substring(Last.Length - 500);
        }

        public static void Clear()
        {
            Count = 0;
            Last = "";
        }
    }
}
#endif
