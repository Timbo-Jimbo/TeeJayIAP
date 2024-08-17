using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Purchasing;

namespace TimboJimbo.InAppPurchasing
{
    public struct ProductDetails
    {
        public string Id;
        public PurchaseAvailability PurchaseAvailability;
        [CanBeNull] public Transaction ActiveTransaction;
        [CanBeNull] public ProductMetadata AdditionalMetadata => UnityProduct?.metadata;
        [CanBeNull] public Product UnityProduct;

        public string GetCostString(string editorFallback = "$1.23")
        {
            if(!PurchaseAvailability.IsAvailable) return "N/A";
            if(Application.isEditor) return editorFallback;
            return AdditionalMetadata?.localizedPriceString ?? "N/A";
        }
    }
}