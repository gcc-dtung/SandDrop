using System;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace GameKit.Core.Foundation.Logging
{
    /// <summary>
    /// Tagged logging: <c>Log.Info("Shop", $"Bought {item}")</c> prints <c>[Shop] Bought …</c>.
    /// <see cref="Info"/> and <see cref="Assert"/> are removed by the compiler from release builds
    /// (call and string building included); they stay in the Editor, Development Builds, or with the
    /// GAMEKIT_VERBOSE_LOGS define. Warnings, errors and exceptions are always kept: they are what
    /// crash / log reporting needs from players' devices.
    /// </summary>
    public static class Log
    {
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("GAMEKIT_VERBOSE_LOGS")]
        public static void Info(string tag, string message, Object context = null)
        {
            Debug.Log(Format(tag, message), context);
        }

        public static void Warning(string tag, string message, Object context = null)
        {
            Debug.LogWarning(Format(tag, message), context);
        }

        public static void Error(string tag, string message, Object context = null)
        {
            Debug.LogError(Format(tag, message), context);
        }

        public static void Exception(Exception exception, Object context = null)
        {
            Debug.LogException(exception, context);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("GAMEKIT_VERBOSE_LOGS")]
        public static void Assert(bool condition, string tag, string message, Object context = null)
        {
            if (!condition)
            {
                Debug.LogAssertion(Format(tag, message), context);
            }
        }

        private static string Format(string tag, string message) => "[" + tag + "] " + message;
    }
}
