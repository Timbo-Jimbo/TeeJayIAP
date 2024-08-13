using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TimboJimbo.InAppPurchasing;
using UnityEngine;
using UnityEngine.Purchasing;

public class TestingScript : MonoBehaviour
{
    public List<IapProductExample> AllExampleProducts;
    
    async Task Awake()
    {
        var initResult = await InAppPurchasing.Initialize(
            // set to false if you'd prefer to initialize the services yourself
            initUnityServices: true,
            // you could ie Debug.isDebugBuild ? IapLogLevel.Verbose : IapLogLevel.Warning
            logLevel: IapLogLevel.Verbose,
            configBuilderModifier: builder =>
            {
                // We generally dont use Unity's catalog functionality. Our projects
                // tend to have a custom list of products that we manage ourselves. 
                // (think: an IAPDefinition Scriptable object etc, so its easy to
                // pass around a reference to it in the Unity Inspector to UI components etc)
                builder.useCatalogProvider = false;

                // add our custom products
                foreach (var iapProduct in AllExampleProducts)
                    builder.AddProduct(iapProduct.Sku, ProductType.Consumable);
            }
        );
        
        Debug.Log($"InAppPurchasing initialized: {initResult}");

        // check if the service is available with this
        if (InAppPurchasing.Available())
        {
            var productToPurchase = AllExampleProducts[0];

            // implementing IIapTransactionListener is optional, all it does is let us
            // pass in objects that represent a product in our project directly in
            // Effectively: ie all the following code is effectively the same as xxx(productToPurchase.sku)
            var isAvailableForPurchase = InAppPurchasing.Products.IsAvailableForPurchase(productToPurchase);
            var costStringToUseInUI = InAppPurchasing.Products.GetCostString(productToPurchase, productToPurchase.ReferencePrice);
            var accessToUnderlyingUnityProduct = InAppPurchasing.Products.Get(productToPurchase);
            var accessToAllProducts = InAppPurchasing.Products.All;
            
            // initiate purchase like so... Remember to wrap InitiateTransaction in a try/catch 
            //as it can throw an exception in certain situations (like if the product does not exists
            // or if a purchase is already in progress)
            var transaction = InAppPurchasing.InitiateTransaction(productToPurchase);

            // note: 'InAppPurchasing.InitiateTransaction' returns you the transaction object
            // immediately, but you'll want to wait until the user finishes performing purcahse
            // before you can access token/transactionId
            await WaitWhile(() => transaction.State == TransactionState.UserPerformingPurchase);
            
            //done! nice...
            if (transaction.State == TransactionState.Failed)
            {
                switch (transaction.FailureReason)
                {
                    case TransactionFailureReason.None:
                        // if transaction.State == TransactionState.Failed, then there will always be a reason!
                        break;
                    case TransactionFailureReason.PurchasingUnavailable:
                    case TransactionFailureReason.ExistingPurchasePending:
                    case TransactionFailureReason.ProductUnavailable:
                    case TransactionFailureReason.SignatureInvalid:
                    case TransactionFailureReason.PaymentDeclined:
                    case TransactionFailureReason.DuplicateTransaction:
                    case TransactionFailureReason.Unknown:
                        // These are errors you can probably tell the user about
                        Debug.Log($"Transaction failed: {transaction.FailureReason}");
                        break;
                    case TransactionFailureReason.UserCancelled:
                        // User cancelled the purchase, probably ignore, return to game etc
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            else if (transaction.State == TransactionState.Deferred)
            {
                // Android deferred purchase. You should send the token to your server
                // and wait for a callback from Google Play to confirm the purchase
                // before rewarding the user.
                
                //valid values:
                var transactionId = transaction.TransactionId;
                var tokenToSendToServerForVerification = transaction.Token;
            }
            else if (transaction.State == TransactionState.ReadyToCollect)
            {
                // not deferred, ready to apply rewards! Since we always apply 
                //rewards on our backend, we just tread all transactions as potentially
                // deferred and send the token to the backend in both cases.
                
                // the backend then checks if the purchase was deferred using the token
                // or grants the user their reward if the purchase was not deferred..
                
                //valid values:
                var transactionId = transaction.TransactionId;
                var tokenToSendToServerForVerification = transaction.Token;
            }
            
            
            //... later, once user has been given their reward
            InAppPurchasing.CompleteTransaction(transaction, wasConsumedAndAcknowledgedRemoted: false);
            
            //... or if you consumed and acknowledged the purchase on the backend (you already called
            // ack + consume using the Google Play Billing library etc..) then you can pass true for
            // the last param so we can skip acknowledging the purchase a second time on the client
            // InAppPurchasing.CompleteTransaction(transaction, wasConsumedAndAcknowledgedRemoted: true);
            
            // ACKNOWLEDGING PURCHASES: VERY IMPORTANT. IF YOU DO NOT ACKNOWLEDGE PURCHASES THEN
            // AFTER A CERTAIN PERIOD OF TIME THE PURCHASE WILL BE REFUNDED AUTOMATICALLY ON GOOGLE PLAY
            // Apple has no concept of 'consuming' or 'acknowledging' purchases, but its simplier to 
            // just write your code to assume that all purchases need to be consumed/acknowledged. 
            // One single unified API is easier to work with for not much additional work. 
            // the consume + ack functionality on your backend could just be no-ops for Apple purchases.
            // (though ideally you'd store the purchase receipt and validate/protect yourself against
            // replay attacks etc)
            
            // Final state, after CompleteTransaction is 'TransactionState.Completed'
            Debug.Log("Final state: " + transaction.State);
        }
    }
    
    [Serializable]
    public class IapProductExample : IInAppPurchaseProductReference
    {
        public string Sku;
        public string ReferencePrice;
        public string GetReferencedProductId() => Sku;
    }
    
    // You'll probably use UniTask or the new Awaitables in newer version of Unity
    private static async ValueTask WaitWhile(Func<bool> condition)
    {
        while (condition())
            await Task.Yield();
    }
}
