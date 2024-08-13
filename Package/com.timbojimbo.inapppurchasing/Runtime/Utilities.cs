using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.Purchasing;

namespace TimboJimbo.InAppPurchasing
{
    internal static class Utilities
    {
        private static readonly InAppPurchasingDelayedCallbackInvoker DelayedCallbackInvoker = new ();
        
        [HideInCallstack]
        public static void SafeInvoke<T>(this UnityEvent<T> unityEvent, T arg0)
        {
            try
            {
                unityEvent?.Invoke(arg0);
            }
            catch (Exception e)
            {
                L.Exception("Failed to invoke event", e);
            }
        }
        
        [HideInCallstack]
        public static void InvokeDelayed(Action action)
        {
            DelayedCallbackInvoker.Invoke(action);
        }

        public static InAppPurchaseToken TryExtractValidationToken(UnifiedReceipt receipt)
        {
            //for other platforms, we have to extract the validation token that our backend expects...:
            //https://docs.unity3d.com/Manual/UnityIAPPurchaseReceipts.html
            if (receipt != null)
            {
                switch (receipt.Store)
                {
                    case "GooglePlay":
                        //For Android: Extract PurchaseToken which is expected byt he backend IAP validation code
                        var googlePlayPayload = JsonUtility.FromJson<GooglePlayPayload>(receipt.Payload);
                        var googlePlayReceipt = JsonUtility.FromJson<GooglePlayReceipt>(googlePlayPayload.json);
                        return new InAppPurchaseToken(googlePlayReceipt.purchaseToken);
                    case "AppleAppStore":
                    case "MacAppStore":
                        //For iOS: the payload on the receipt is the Base64 encoded receipt expected by the backend IAP validation code
                        //(Except for iOS versions <7, but we dont support that)
                        return new InAppPurchaseToken(receipt.Payload);
                }

                if (receipt.Store.ToLower().Contains("fake"))
                    return InAppPurchaseToken.Random; // for testing
            }
            
            return InAppPurchaseToken.Invalid;
        }
        
        public static async ValueTask<T> WithCancellationToken<T>(this Task<T> task, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>();
            using (ct.Register(() => tcs.TrySetResult(true)))
            {
                if (task != await Task.WhenAny(task, tcs.Task))
                    throw new OperationCanceledException(ct);
            }

            return await task;
        }

        public static async ValueTask WithCancellationToken(this Task task, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>();
            using (ct.Register(() => tcs.TrySetResult(true)))
            {
                if (task != await Task.WhenAny(task, tcs.Task))
                    throw new OperationCanceledException(ct);
            }

            await task;
        }
        
        public static async ValueTask WaitUntil(Func<bool> predicate, CancellationToken ct)
        {
            while (!predicate())
            {
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
            }
        }
        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            var updateIndex = Array.FindIndex(playerLoop.subSystemList, x => x.type == typeof(Update));
            var customSystem = new PlayerLoopSystem
            {
                type = typeof(InAppPurchasingDelayedCallbackInvoker),
                updateDelegate = DelayedCallbackInvoker.ProcessCallbacks
            };
        
            var newUpdateList = new List<PlayerLoopSystem>(playerLoop.subSystemList[updateIndex].subSystemList);
            newUpdateList.Insert(0, customSystem);
            playerLoop.subSystemList[updateIndex].subSystemList = newUpdateList.ToArray();
        
            PlayerLoop.SetPlayerLoop(playerLoop);
        }
        
        private class InAppPurchasingDelayedCallbackInvoker
        {
            private List<Action> _actions = new List<Action>();
            
            public void Invoke(Action action)
            {
                _actions.Add(action);
            }
            
            [HideInCallstack]
            public void ProcessCallbacks()
            {
                foreach (var action in _actions)
                {
                    try
                    {
                        action();
                    }
                    catch(Exception e)
                    {
                        L.Exception("Failed to invoke delayed callback", e);
                    }
                }
                
                _actions.Clear();
            }
        }
        //Android specific payload class, structure documented here
        //https://docs.unity3d.com/Manual/UnityIAPPurchaseReceipts.html
        [Serializable]
        private class GooglePlayPayload
        {
            public string json;
            public string signature;
        }

        //Android specific
        //JSON fields for INAPP_PURCHASE_DATA
        //https://docs.unity3d.com/Manual/UnityIAPPurchaseReceipts.html
        [Serializable]
        private class GooglePlayReceipt
        {
            public string purchaseToken;
        }
    }
}