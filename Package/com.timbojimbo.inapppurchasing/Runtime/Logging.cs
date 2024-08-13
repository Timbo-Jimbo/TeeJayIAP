using System;
using UnityEngine;

namespace TimboJimbo.InAppPurchasing
{
    internal static class L
    {
        public static IapLogLevel LogLevel = IapLogLevel.Info;
            
        [HideInCallstack]
        public static void Verbose(string message)
        {
            if(LogLevel > IapLogLevel.Verbose) return;
            Debug.Log($"{nameof(InAppPurchasing)}: {message}");
        }
            
        [HideInCallstack]
        public static void Info(string message)
        {
            if(LogLevel > IapLogLevel.Info) return;
            Debug.Log($"{nameof(InAppPurchasing)}: {message}");
        }
        [HideInCallstack]
        public static void Warning(string message)
        {
            if(LogLevel > IapLogLevel.Warning) return;
            Debug.LogWarning($"{nameof(InAppPurchasing)}: {message}");
        }
            
        [HideInCallstack]
        public static void Error(string message)
        {
            if(LogLevel > IapLogLevel.Error) return;
            Debug.LogError($"{nameof(InAppPurchasing)}: {message}");
        }

        [HideInCallstack]
        public static void Exception(string message, Exception e)
        {
            Error(message);
            Debug.LogException(e);
        }
    }
    
    public enum IapLogLevel
    {
        Verbose,
        Info,
        Warning,
        Error
    }
}